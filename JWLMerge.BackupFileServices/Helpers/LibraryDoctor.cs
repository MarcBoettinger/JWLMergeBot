using System;
using System.Collections.Generic;
using System.Linq;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public enum HealthSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum HealthIssueKind
{
    // Critical: can make JW Library refuse the restore
    DuplicateLocations,
    OrphanBlockRanges,
    DuplicateBlockRanges,
    HighlightsWithoutLocation,
    NotesWithBrokenLinks,
    BookmarksWithoutLocation,
    InputFieldsWithoutLocation,
    BrokenTagLinks,
    DuplicateTagLinks,
    InvalidOrDuplicateGuids,

    // Warning: clutter
    EmptyNotes,
    DuplicateNotes,
    UnusedLocations,

    // Info: reported only, never touched automatically
    UnusedTags,
    HighlightsWithoutRanges,
}

public sealed class HealthIssue
{
    public HealthIssue(HealthIssueKind kind, HealthSeverity severity, int count, bool fixable)
    {
        Kind = kind;
        Severity = severity;
        Count = count;
        Fixable = fixable;
    }

    public HealthIssueKind Kind { get; }
    public HealthSeverity Severity { get; }
    public int Count { get; }
    public bool Fixable { get; }
}

public sealed class HealthReport
{
    public List<HealthIssue> Issues { get; } = new();

    public int Notes { get; set; }
    public int Highlights { get; set; }
    public int Bookmarks { get; set; }
    public int Tags { get; set; }

    public bool HasCritical => Issues.Any(i => i.Severity == HealthSeverity.Critical);

    public bool IsHealthy => !Issues.Any(i => i.Severity != HealthSeverity.Info);

    public bool HasFixable => Issues.Any(i => i.Fixable);
}

/// <summary>
/// Scans a backup for structural problems and clutter, and repairs the safe ones.
/// The scan deliberately does not use the Database lookup indexes, because those
/// throw on exactly the kind of corruption we are looking for.
/// </summary>
public sealed class LibraryDoctor
{
    // ------------------------------------------------------------------ Scan

    public HealthReport Scan(BackupFile backup)
    {
        if (backup == null)
        {
            throw new ArgumentNullException(nameof(backup));
        }

        var db = backup.Database;
        var report = new HealthReport
        {
            Notes = db.Notes.Count,
            Highlights = db.UserMarks.Count,
            Bookmarks = db.Bookmarks.Count,
            Tags = db.Tags.Count(t => t.Type == 1),
        };

        var locationIds = new HashSet<int>(db.Locations.Select(l => l.LocationId));
        var userMarkIds = new HashSet<int>(db.UserMarks.Select(u => u.UserMarkId));
        var noteIds = new HashSet<int>(db.Notes.Select(n => n.NoteId));
        var tagIds = new HashSet<int>(db.Tags.Select(t => t.TagId));
        var playlistItemIds = new HashSet<int>(db.PlaylistItems.Select(p => p.PlaylistItemId));

        Add(report, HealthIssueKind.DuplicateLocations, HealthSeverity.Critical, FindDuplicateLocations(db).Count, true);

        Add(report, HealthIssueKind.OrphanBlockRanges, HealthSeverity.Critical,
            db.BlockRanges.Count(r => !userMarkIds.Contains(r.UserMarkId)), true);

        Add(report, HealthIssueKind.DuplicateBlockRanges, HealthSeverity.Critical,
            db.BlockRanges.Where(r => userMarkIds.Contains(r.UserMarkId)).GroupBy(r => r.UserMarkId).Sum(g => g.Count() - 1), true);

        Add(report, HealthIssueKind.HighlightsWithoutLocation, HealthSeverity.Critical,
            db.UserMarks.Count(u => !locationIds.Contains(u.LocationId)), true);

        Add(report, HealthIssueKind.NotesWithBrokenLinks, HealthSeverity.Critical,
            db.Notes.Count(n =>
                (n.LocationId != null && !locationIds.Contains(n.LocationId.Value)) ||
                (n.UserMarkId != null && !userMarkIds.Contains(n.UserMarkId.Value))), true);

        Add(report, HealthIssueKind.BookmarksWithoutLocation, HealthSeverity.Critical,
            db.Bookmarks.Count(b => !locationIds.Contains(b.LocationId) || !locationIds.Contains(b.PublicationLocationId)), true);

        Add(report, HealthIssueKind.InputFieldsWithoutLocation, HealthSeverity.Critical,
            db.InputFields.Count(f => !locationIds.Contains(f.LocationId)), true);

        Add(report, HealthIssueKind.BrokenTagLinks, HealthSeverity.Critical,
            db.TagMaps.Count(m => IsTagMapBroken(m, tagIds, noteIds, locationIds, playlistItemIds)), true);

        Add(report, HealthIssueKind.DuplicateTagLinks, HealthSeverity.Critical,
            CountDuplicateTagMaps(db), true);

        Add(report, HealthIssueKind.InvalidOrDuplicateGuids, HealthSeverity.Critical,
            CountBadGuids(db.Notes.Select(n => n.Guid)) + CountBadGuids(db.UserMarks.Select(u => u.UserMarkGuid)), true);

        // Clutter
        var emptyNotes = db.Notes.Where(IsEmptyNote).ToList();
        Add(report, HealthIssueKind.EmptyNotes, HealthSeverity.Warning, emptyNotes.Count, true);

        Add(report, HealthIssueKind.DuplicateNotes, HealthSeverity.Warning,
            db.Notes.Where(n => !IsEmptyNote(n)).GroupBy(NoteKey).Sum(g => g.Count() - 1), true);

        var inUse = GetLocationIdsInUse(db);
        Add(report, HealthIssueKind.UnusedLocations, HealthSeverity.Warning,
            db.Locations.Count(l => !inUse.Contains(l.LocationId)), true);

        // Info only
        var usedTagIds = new HashSet<int>(db.TagMaps.Select(m => m.TagId));
        Add(report, HealthIssueKind.UnusedTags, HealthSeverity.Info,
            db.Tags.Count(t => t.Type == 1 && !usedTagIds.Contains(t.TagId)), false);

        var userMarksWithRanges = new HashSet<int>(db.BlockRanges.Select(r => r.UserMarkId));
        var userMarksWithNotes = new HashSet<int>(db.Notes.Where(n => n.UserMarkId != null).Select(n => n.UserMarkId!.Value));
        Add(report, HealthIssueKind.HighlightsWithoutRanges, HealthSeverity.Info,
            db.UserMarks.Count(u => !userMarksWithRanges.Contains(u.UserMarkId) && !userMarksWithNotes.Contains(u.UserMarkId)), false);

        return report;
    }

    // ---------------------------------------------------------------- Repair

    /// <summary>
    /// Repairs everything flagged as fixable, in memory. The caller must write the
    /// result with IBackupFileService.WriteNewDatabase (which also recomputes the manifest hash).
    /// </summary>
    /// <returns>Number of rows changed or removed.</returns>
    public int Repair(BackupFile backup)
    {
        if (backup == null)
        {
            throw new ArgumentNullException(nameof(backup));
        }

        var db = backup.Database;
        var changes = 0;

        changes += MergeDuplicateLocations(db);
        changes += FixGuids(db);

        // Notes: empty ones first, then exact duplicates (re-pointing their tags to the survivor)
        changes += RemoveEmptyNotes(db);
        changes += RemoveDuplicateNotes(db);

        // Highlights: drop those without a location, plus their ranges; clear links from notes
        var locationIds = new HashSet<int>(db.Locations.Select(l => l.LocationId));
        var badMarks = new HashSet<int>(db.UserMarks.Where(u => !locationIds.Contains(u.LocationId)).Select(u => u.UserMarkId));
        changes += db.UserMarks.RemoveAll(u => badMarks.Contains(u.UserMarkId));

        var userMarkIds = new HashSet<int>(db.UserMarks.Select(u => u.UserMarkId));

        changes += db.BlockRanges.RemoveAll(r => !userMarkIds.Contains(r.UserMarkId));
        changes += RemoveDuplicateBlockRanges(db);

        foreach (var note in db.Notes)
        {
            if (note.LocationId != null && !locationIds.Contains(note.LocationId.Value))
            {
                note.LocationId = null;
                changes++;
            }

            if (note.UserMarkId != null && !userMarkIds.Contains(note.UserMarkId.Value))
            {
                note.UserMarkId = null;
                changes++;
            }
        }

        changes += db.Bookmarks.RemoveAll(b => !locationIds.Contains(b.LocationId) || !locationIds.Contains(b.PublicationLocationId));
        changes += db.InputFields.RemoveAll(f => !locationIds.Contains(f.LocationId));

        var noteIds = new HashSet<int>(db.Notes.Select(n => n.NoteId));
        var tagIds = new HashSet<int>(db.Tags.Select(t => t.TagId));
        var playlistItemIds = new HashSet<int>(db.PlaylistItems.Select(p => p.PlaylistItemId));
        changes += db.TagMaps.RemoveAll(m => IsTagMapBroken(m, tagIds, noteIds, locationIds, playlistItemIds));
        changes += RemoveDuplicateTagMaps(db);

        // Finally, locations nobody uses any more
        var inUse = GetLocationIdsInUse(db);
        changes += db.Locations.RemoveAll(l => !inUse.Contains(l.LocationId));

        // Throws if something is still inconsistent: the caller reports it instead of writing a bad file
        db.CheckValidity();

        return changes;
    }

    // --------------------------------------------------------------- Helpers

    private static void Add(HealthReport report, HealthIssueKind kind, HealthSeverity severity, int count, bool fixable)
    {
        if (count > 0)
        {
            report.Issues.Add(new HealthIssue(kind, severity, count, fixable));
        }
    }

    private static string LocationKey(Location l)
    {
        return $"{l.KeySymbol}|{l.IssueTagNumber}|{l.MepsLanguage}|{l.DocumentId!.Value}|{l.Track!.Value}|{l.Type}";
    }

    /// <summary>Maps duplicate location id to the id that is kept. Same rule as the SQLite unique index.</summary>
    private static Dictionary<int, int> FindDuplicateLocations(Database db)
    {
        var kept = new Dictionary<string, int>();
        var remap = new Dictionary<int, int>();

        foreach (var loc in db.Locations)
        {
            // NULL is unique in SQLite, so such rows can't collide
            if (loc.Track == null || loc.DocumentId == null)
            {
                continue;
            }

            var key = LocationKey(loc);
            if (kept.TryGetValue(key, out var keptId))
            {
                remap[loc.LocationId] = keptId;
            }
            else
            {
                kept.Add(key, loc.LocationId);
            }
        }

        return remap;
    }

    private static int MergeDuplicateLocations(Database db)
    {
        var remap = FindDuplicateLocations(db);
        if (remap.Count == 0)
        {
            return 0;
        }

        int Map(int id) => remap.TryGetValue(id, out var to) ? to : id;
        int? MapN(int? id) => id == null ? null : Map(id.Value);

        foreach (var n in db.Notes) n.LocationId = MapN(n.LocationId);
        foreach (var u in db.UserMarks) u.LocationId = Map(u.LocationId);
        foreach (var b in db.Bookmarks) { b.LocationId = Map(b.LocationId); b.PublicationLocationId = Map(b.PublicationLocationId); }
        foreach (var m in db.TagMaps) m.LocationId = MapN(m.LocationId);
        foreach (var f in db.InputFields) f.LocationId = Map(f.LocationId);
        foreach (var p in db.PlaylistItemLocationMaps) p.LocationId = Map(p.LocationId);

        return db.Locations.RemoveAll(l => remap.ContainsKey(l.LocationId));
    }

    private static bool IsTagMapBroken(TagMap m, HashSet<int> tagIds, HashSet<int> noteIds, HashSet<int> locationIds, HashSet<int> playlistItemIds)
    {
        return !tagIds.Contains(m.TagId) ||
               (m.NoteId != null && !noteIds.Contains(m.NoteId.Value)) ||
               (m.LocationId != null && !locationIds.Contains(m.LocationId.Value)) ||
               (m.PlaylistItemId != null && !playlistItemIds.Contains(m.PlaylistItemId.Value));
    }

    private static string TagMapKey(TagMap m)
    {
        return $"{m.TagId}|{m.NoteId}|{m.LocationId}|{m.PlaylistItemId}";
    }

    private static int CountDuplicateTagMaps(Database db)
    {
        return db.TagMaps.GroupBy(TagMapKey).Sum(g => g.Count() - 1);
    }

    private static int RemoveDuplicateTagMaps(Database db)
    {
        var seen = new HashSet<string>();
        return db.TagMaps.RemoveAll(m => !seen.Add(TagMapKey(m)));
    }

    private static int RemoveDuplicateBlockRanges(Database db)
    {
        // one BlockRange row per UserMark is what the app expects
        var seen = new HashSet<int>();
        return db.BlockRanges.RemoveAll(r => !seen.Add(r.UserMarkId));
    }

    private static int CountBadGuids(IEnumerable<string?> guids)
    {
        var seen = new HashSet<Guid>();
        var bad = 0;
        foreach (var s in guids)
        {
            if (!Guid.TryParse(s, out var g) || !seen.Add(g))
            {
                bad++;
            }
        }

        return bad;
    }

    private static int FixGuids(Database db)
    {
        var fixes = 0;

        var seenNotes = new HashSet<Guid>();
        foreach (var n in db.Notes)
        {
            if (!Guid.TryParse(n.Guid, out var g) || !seenNotes.Add(g))
            {
                n.Guid = NewUniqueGuid(seenNotes);
                fixes++;
            }
        }

        var seenMarks = new HashSet<Guid>();
        foreach (var u in db.UserMarks)
        {
            if (!Guid.TryParse(u.UserMarkGuid, out var g) || !seenMarks.Add(g))
            {
                u.UserMarkGuid = NewUniqueGuid(seenMarks);
                fixes++;
            }
        }

        return fixes;
    }

    private static string NewUniqueGuid(HashSet<Guid> seen)
    {
        Guid g;
        do
        {
            g = Guid.NewGuid();
        }
        while (!seen.Add(g));

        return g.ToString();
    }

    private static bool IsEmptyNote(Note n)
    {
        return string.IsNullOrWhiteSpace(n.Title) && string.IsNullOrWhiteSpace(n.Content);
    }

    private static string NoteKey(Note n)
    {
        return $"{n.LocationId}|{n.BlockType}|{n.BlockIdentifier}|{n.Title?.Trim()}|{n.Content?.Trim()}";
    }

    private static int RemoveEmptyNotes(Database db)
    {
        var ids = new HashSet<int>(db.Notes.Where(IsEmptyNote).Select(n => n.NoteId));
        if (ids.Count == 0)
        {
            return 0;
        }

        db.TagMaps.RemoveAll(m => m.NoteId != null && ids.Contains(m.NoteId.Value));
        return db.Notes.RemoveAll(n => ids.Contains(n.NoteId));
    }

    private static int RemoveDuplicateNotes(Database db)
    {
        // keep the first occurrence, but prefer the most recently modified copy
        var survivors = new Dictionary<string, Note>();
        var removedToSurvivor = new Dictionary<int, int>();

        foreach (var note in db.Notes)
        {
            var key = NoteKey(note);
            if (survivors.TryGetValue(key, out var kept))
            {
                removedToSurvivor[note.NoteId] = kept.NoteId;
            }
            else
            {
                survivors.Add(key, note);
            }
        }

        if (removedToSurvivor.Count == 0)
        {
            return 0;
        }

        // move tags of the removed copies to the survivor (duplicates are dropped afterwards)
        foreach (var m in db.TagMaps)
        {
            if (m.NoteId != null && removedToSurvivor.TryGetValue(m.NoteId.Value, out var to))
            {
                m.NoteId = to;
            }
        }

        return db.Notes.RemoveAll(n => removedToSurvivor.ContainsKey(n.NoteId));
    }

    private static HashSet<int> GetLocationIdsInUse(Database db)
    {
        var used = new HashSet<int>();

        foreach (var b in db.Bookmarks) { used.Add(b.LocationId); used.Add(b.PublicationLocationId); }
        foreach (var n in db.Notes) { if (n.LocationId != null) used.Add(n.LocationId.Value); }
        foreach (var u in db.UserMarks) used.Add(u.LocationId);
        foreach (var m in db.TagMaps) { if (m.LocationId != null) used.Add(m.LocationId.Value); }
        foreach (var f in db.InputFields) used.Add(f.LocationId);
        foreach (var p in db.PlaylistItemLocationMaps) used.Add(p.LocationId);

        return used;
    }
}
