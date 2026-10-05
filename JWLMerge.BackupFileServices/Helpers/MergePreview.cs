using System;
using System.Collections.Generic;
using System.Linq;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public sealed class NoteVersion
{
    public NoteVersion(string title, string content, string? lastModified, DateTime modified)
    {
        Title = title;
        Content = content;
        LastModified = lastModified;
        Modified = modified;
    }

    public string Title { get; }
    public string Content { get; }
    public string? LastModified { get; }
    public DateTime Modified { get; }
}

/// <summary>A note that exists in both files (same GUID) with different text.</summary>
public sealed class NoteConflict
{
    public NoteConflict(string guid, string where, NoteVersion stored, NoteVersion incoming)
    {
        Guid = guid;
        Where = where;
        Stored = stored;
        Incoming = incoming;
    }

    public string Guid { get; }

    /// <summary>Human readable place of the note, e.g. "John 3:16".</summary>
    public string Where { get; }

    public NoteVersion Stored { get; }
    public NoteVersion Incoming { get; }

    /// <summary>What a plain merge does: the more recent edit wins, a tie keeps the stored version.</summary>
    public bool IncomingWinsByDefault => Stored.Modified < Incoming.Modified;
}

public sealed class MergePreviewResult
{
    public int NewNotes { get; set; }
    public int NewHighlights { get; set; }
    public int NewBookmarks { get; set; }
    public int NewTags { get; set; }
    public int NewPlaylistItems { get; set; }

    /// <summary>Notes present in both files with identical text.</summary>
    public int UnchangedNotes { get; set; }

    public List<NoteConflict> Conflicts { get; } = new();

    public bool HasNews => NewNotes + NewHighlights + NewBookmarks + NewTags + NewPlaylistItems > 0;

    public bool HasChanges => HasNews || Conflicts.Count > 0;
}

/// <summary>
/// Read only prediction of what merging <c>incoming</c> into <c>stored</c> will do, and the means to
/// override which version of a conflicting note wins. Counts are computed before the merger's own
/// clean-up, so they are a close estimate rather than a promise.
/// </summary>
public sealed class MergePreview
{
    public MergePreviewResult Compute(BackupFile stored, BackupFile incoming)
    {
        if (stored == null) throw new ArgumentNullException(nameof(stored));
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));

        var a = stored.Database;
        var b = incoming.Database;
        var result = new MergePreviewResult();

        var storedLocations = a.Locations.GroupBy(l => l.LocationId).ToDictionary(g => g.Key, g => g.First());
        var incomingLocations = b.Locations.GroupBy(l => l.LocationId).ToDictionary(g => g.Key, g => g.First());

        // Notes (matched by GUID, exactly like the merger)
        var storedNotes = new Dictionary<Guid, Note>();
        foreach (var n in a.Notes)
        {
            if (Guid.TryParse(n.Guid, out var g))
            {
                storedNotes[g] = n;
            }
        }

        var seenIncoming = new HashSet<Guid>();
        foreach (var n in b.Notes)
        {
            if (!Guid.TryParse(n.Guid, out var g) || !seenIncoming.Add(g))
            {
                continue;
            }

            if (!storedNotes.TryGetValue(g, out var existing))
            {
                result.NewNotes++;
                continue;
            }

            if (Same(existing.Title, n.Title) && Same(existing.Content, n.Content))
            {
                result.UnchangedNotes++;
                continue;
            }

            result.Conflicts.Add(new NoteConflict(
                g.ToString(),
                LocationDescriber.Describe(n, incomingLocations) ?? LocationDescriber.Describe(existing, storedLocations) ?? "?",
                ToVersion(existing),
                ToVersion(n)));
        }

        // Highlights
        var storedMarks = new HashSet<Guid>();
        foreach (var u in a.UserMarks)
        {
            if (Guid.TryParse(u.UserMarkGuid, out var g))
            {
                storedMarks.Add(g);
            }
        }

        var seenMarks = new HashSet<Guid>();
        foreach (var u in b.UserMarks)
        {
            if (Guid.TryParse(u.UserMarkGuid, out var g) && seenMarks.Add(g) && !storedMarks.Contains(g))
            {
                result.NewHighlights++;
            }
        }

        // Bookmarks (same location + publication location, compared by value)
        var storedBookmarks = new HashSet<string>();
        foreach (var bm in a.Bookmarks)
        {
            var key = BookmarkKey(bm, storedLocations);
            if (key != null) storedBookmarks.Add(key);
        }

        var seenBookmarks = new HashSet<string>();
        foreach (var bm in b.Bookmarks)
        {
            var key = BookmarkKey(bm, incomingLocations);
            if (key != null && seenBookmarks.Add(key) && !storedBookmarks.Contains(key))
            {
                result.NewBookmarks++;
            }
        }

        // Tags (type + name)
        var storedTags = new HashSet<string>(a.Tags.Select(t => t.Type + "|" + t.Name));
        result.NewTags = b.Tags.Select(t => t.Type + "|" + t.Name).Distinct().Count(k => !storedTags.Contains(k));

        // Playlist items (label + trim offsets, like the merger)
        var storedItems = new HashSet<string>(a.PlaylistItems.Select(PlaylistKey));
        result.NewPlaylistItems = b.PlaylistItems.Select(PlaylistKey).Distinct().Count(k => !storedItems.Contains(k));

        return result;
    }

    /// <summary>
    /// Applies the user's choices to a merged backup. Only the text and date of the listed notes change.
    /// If the chosen version is the older one, its date is set to now so that the choice also wins in later merges.
    /// </summary>
    /// <param name="useIncoming">Note GUID to true (take the new file's version) or false (keep the stored version).</param>
    /// <returns>Number of notes set to something different from what the plain merge produced.</returns>
    public int ApplyChoices(BackupFile merged, IEnumerable<NoteConflict> conflicts, IReadOnlyDictionary<string, bool> useIncoming)
    {
        if (merged == null) throw new ArgumentNullException(nameof(merged));

        var byGuid = new Dictionary<Guid, Note>();
        foreach (var n in merged.Database.Notes)
        {
            if (Guid.TryParse(n.Guid, out var g))
            {
                byGuid[g] = n;
            }
        }

        var changed = 0;
        foreach (var c in conflicts)
        {
            if (!useIncoming.TryGetValue(c.Guid, out var takeIncoming) ||
                !Guid.TryParse(c.Guid, out var g) ||
                !byGuid.TryGetValue(g, out var note))
            {
                continue;
            }

            var chosen = takeIncoming ? c.Incoming : c.Stored;
            var other = takeIncoming ? c.Stored : c.Incoming;

            if (takeIncoming == c.IncomingWinsByDefault)
            {
                continue; // the plain merge already did this
            }

            note.Title = chosen.Title;
            note.Content = chosen.Content;
            note.LastModified = chosen.Modified < other.Modified
                ? Database.GetDateTimeUtcAsDbString(DateTime.UtcNow)
                : chosen.LastModified;
            changed++;
        }

        return changed;
    }

    private static bool Same(string? x, string? y)
    {
        return string.Equals(x ?? string.Empty, y ?? string.Empty, StringComparison.Ordinal);
    }

    private static NoteVersion ToVersion(Note n)
    {
        return new NoteVersion(n.Title ?? string.Empty, n.Content ?? string.Empty, n.LastModified, n.GetLastModifiedDateTime());
    }

    private static string LocationKey(Location l)
    {
        return $"{l.KeySymbol}|{l.IssueTagNumber}|{l.MepsLanguage}|{l.Type}|{l.BookNumber ?? -1}|{l.ChapterNumber ?? -1}|{l.DocumentId ?? -1}|{l.Track ?? -1}";
    }

    private static string? BookmarkKey(Bookmark bm, Dictionary<int, Location> locations)
    {
        if (!locations.TryGetValue(bm.LocationId, out var l1) || !locations.TryGetValue(bm.PublicationLocationId, out var l2))
        {
            return null;
        }

        return LocationKey(l1) + "##" + LocationKey(l2);
    }

    private static string PlaylistKey(PlaylistItem p)
    {
        return $"{p.Label}|{p.StartTrimOffsetTicks ?? -1}|{p.EndTrimOffsetTicks ?? -1}";
    }
}
