using System;
using System.Collections.Generic;
using System.Linq;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public sealed class ExtractResult
{
    public int Notes { get; set; }
    public int Highlights { get; set; }
    public int Tags { get; set; }
}

/// <summary>
/// Cuts a small backup out of a loaded one: only some notes stay, together with the highlights attached to
/// them and the tags they carry. Bookmarks, input fields and playlists are left out. Works on the
/// backup it is given, so pass a freshly loaded copy and write it to a NEW file.
/// </summary>
public sealed class LibraryExtractor
{
    /// <summary>Keep the notes that carry at least one of the given tags.</summary>
    public ExtractResult KeepNotesWithTags(BackupFile backup, IReadOnlyCollection<int> tagIds)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        var wanted = new HashSet<int>(tagIds);
        var keep = new HashSet<int>(
            backup.Database.TagMaps
                .Where(m => m.NoteId != null && wanted.Contains(m.TagId))
                .Select(m => m.NoteId!.Value));
        return Keep(backup.Database, keep);
    }

    /// <summary>Keep the notes last edited from <paramref name="fromInclusive"/> up to (not including) <paramref name="toExclusive"/>.</summary>
    public ExtractResult KeepNotesModifiedBetween(BackupFile backup, DateTime fromInclusive, DateTime toExclusive)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        var keep = new HashSet<int>();
        foreach (var n in backup.Database.Notes)
        {
            var modified = n.GetLastModifiedDateTime();
            if (modified >= fromInclusive && modified < toExclusive)
            {
                keep.Add(n.NoteId);
            }
        }

        return Keep(backup.Database, keep);
    }

    /// <summary>A service year runs from 1 September to 31 August. <paramref name="yearsBack"/> 0 is the current one.</summary>
    public static (DateTime From, DateTime To) ServiceYear(DateTime today, int yearsBack)
    {
        var startYear = (today.Month >= 9 ? today.Year : today.Year - 1) - yearsBack;
        var from = new DateTime(startYear, 9, 1);
        return (from, from.AddYears(1));
    }

    private static ExtractResult Keep(Database db, HashSet<int> keepNoteIds)
    {
        db.Notes.RemoveAll(n => !keepNoteIds.Contains(n.NoteId));

        // tags: only links to the kept notes survive
        db.TagMaps.RemoveAll(m => m.NoteId == null || !keepNoteIds.Contains(m.NoteId.Value));
        var usedTags = new HashSet<int>(db.TagMaps.Select(m => m.TagId));
        db.Tags.RemoveAll(t => t.Type == 1 && !usedTags.Contains(t.TagId));

        var perTag = db.TagMaps.GroupBy(m => m.TagId);
        foreach (var g in perTag)
        {
            var pos = 0;
            foreach (var m in g.OrderBy(x => x.Position))
            {
                m.Position = pos++;
            }
        }

        // highlights: only those attached to a kept note
        var keepMarks = new HashSet<int>(db.Notes.Where(n => n.UserMarkId != null).Select(n => n.UserMarkId!.Value));
        db.UserMarks.RemoveAll(u => !keepMarks.Contains(u.UserMarkId));
        db.BlockRanges.RemoveAll(r => !keepMarks.Contains(r.UserMarkId));

        // everything else that is not about notes
        db.Bookmarks.Clear();
        db.InputFields.Clear();
        db.PlaylistItemMarkerBibleVerseMaps.Clear();
        db.PlaylistItemMarkerParagraphMaps.Clear();
        db.PlaylistItemMarkers.Clear();
        db.PlaylistItemIndependentMediaMaps.Clear();
        db.PlaylistItemLocationMaps.Clear();
        db.PlaylistItems.Clear();
        db.IndependentMedias.Clear();

        // places nobody refers to any more
        var usedLocations = new HashSet<int>();
        foreach (var n in db.Notes) if (n.LocationId != null) usedLocations.Add(n.LocationId.Value);
        foreach (var u in db.UserMarks) usedLocations.Add(u.LocationId);
        foreach (var m in db.TagMaps) if (m.LocationId != null) usedLocations.Add(m.LocationId.Value);
        db.Locations.RemoveAll(l => !usedLocations.Contains(l.LocationId));

        return new ExtractResult
        {
            Notes = db.Notes.Count,
            Highlights = db.UserMarks.Count,
            Tags = db.Tags.Count(t => t.Type == 1),
        };
    }
}
