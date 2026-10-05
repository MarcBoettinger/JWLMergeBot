using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public sealed class NamedCount
{
    public NamedCount(string name, int count)
    {
        Name = name;
        Count = count;
    }

    public string Name { get; }
    public int Count { get; }
}

public sealed class StudyStatsResult
{
    // Totals
    public int Notes { get; set; }
    public int Highlights { get; set; }
    public int Bookmarks { get; set; }
    public int Tags { get; set; }
    public int NotesWords { get; set; }

    // Activity (based on the note "last modified" dates: highlights carry no date in the database)
    public DateTime? FirstActivity { get; set; }
    public DateTime? LastActivity { get; set; }
    public int ActiveDays { get; set; }
    public int LongestStreak { get; set; }
    public int CurrentStreak { get; set; }
    public int NotesLast30Days { get; set; }
    public DayOfWeek? BusiestWeekday { get; set; }
    public int[] NotesPerWeekday { get; } = new int[7]; // index = (int)DayOfWeek

    /// <summary>Notes per month for the last 12 months, oldest first (month = first day of the month).</summary>
    public List<KeyValuePair<DateTime, int>> NotesPerMonth { get; } = new();

    public int AverageWordsPerNote { get; set; }
    public int LongestNoteWords { get; set; }

    // Bible coverage
    public int BibleBooksTouched { get; set; }
    public int BibleChaptersTouched { get; set; }
    public List<NamedCount> TopBibleBooks { get; } = new();
    public List<NamedCount> TopHighlightedChapters { get; } = new();

    // Other
    public List<NamedCount> TopTags { get; } = new();
    public List<NamedCount> TopPublications { get; } = new();

    /// <summary>Highlights per colour index (1-6).</summary>
    public int[] HighlightColors { get; } = new int[7];

    public List<string> Milestones { get; } = new();
}

/// <summary>
/// Turns a backup into a summary of the study it contains. Read only, and tolerant of
/// broken files: it never uses the Database lookup indexes.
/// </summary>
public sealed class StudyStats
{
    private const int BibleBookCount = 66;

    public StudyStatsResult Compute(BackupFile backup, DateTime today)
    {
        if (backup == null)
        {
            throw new ArgumentNullException(nameof(backup));
        }

        var db = backup.Database;
        var r = new StudyStatsResult
        {
            Notes = db.Notes.Count,
            Highlights = db.UserMarks.Count,
            Bookmarks = db.Bookmarks.Count,
            Tags = db.Tags.Count(t => t.Type == 1),
        };

        var locations = new Dictionary<int, Location>();
        foreach (var l in db.Locations)
        {
            locations[l.LocationId] = l;
        }

        ComputeWords(db, r);
        ComputeActivity(db, r, today.Date);
        ComputeBible(db, r, locations);
        ComputeHighlightedChapters(db, r, locations);
        ComputeTags(db, r);
        ComputePublications(db, r, locations);

        foreach (var u in db.UserMarks)
        {
            if (u.ColorIndex >= 1 && u.ColorIndex <= 6)
            {
                r.HighlightColors[u.ColorIndex]++;
            }
        }

        ComputeMilestones(r);
        return r;
    }

    private static void ComputeWords(Database db, StudyStatsResult r)
    {
        var total = 0;
        var withText = 0;
        var longest = 0;
        foreach (var n in db.Notes)
        {
            var w = CountWords(n.Title) + CountWords(n.Content);
            total += w;
            if (w > 0)
            {
                withText++;
                longest = Math.Max(longest, w);
            }
        }

        r.NotesWords = total;
        r.LongestNoteWords = longest;
        r.AverageWordsPerNote = withText == 0 ? 0 : (int)Math.Round((double)total / withText);
    }

    private static int CountWords(string? s)
    {
        return string.IsNullOrWhiteSpace(s)
            ? 0
            : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static void ComputeActivity(Database db, StudyStatsResult r, DateTime today)
    {
        var days = new SortedSet<DateTime>();
        var perMonth = new Dictionary<DateTime, int>();

        foreach (var n in db.Notes)
        {
            if (string.IsNullOrWhiteSpace(n.LastModified) ||
                !DateTime.TryParse(n.LastModified, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            {
                continue;
            }

            var d = dt.Date;
            days.Add(d);
            r.NotesPerWeekday[(int)d.DayOfWeek]++;

            var month = new DateTime(d.Year, d.Month, 1);
            perMonth[month] = perMonth.TryGetValue(month, out var pm) ? pm + 1 : 1;

            if (d > today.AddDays(-30) && d <= today)
            {
                r.NotesLast30Days++;
            }
        }

        // the last 12 months, including empty ones, so that gaps are visible
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        for (var i = 11; i >= 0; i--)
        {
            var m = thisMonth.AddMonths(-i);
            r.NotesPerMonth.Add(new KeyValuePair<DateTime, int>(m, perMonth.TryGetValue(m, out var c) ? c : 0));
        }

        if (days.Count == 0)
        {
            return;
        }

        r.FirstActivity = days.Min;
        r.LastActivity = days.Max;
        r.ActiveDays = days.Count;

        // longest streak of consecutive days
        var longest = 0;
        var run = 0;
        DateTime? prev = null;
        foreach (var d in days)
        {
            run = prev != null && (d - prev.Value).Days == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }

        r.LongestStreak = longest;

        // current streak: ends today or yesterday
        var cursor = days.Contains(today) ? today : today.AddDays(-1);
        var current = 0;
        while (days.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        r.CurrentStreak = current;

        var max = r.NotesPerWeekday.Max();
        if (max > 0)
        {
            r.BusiestWeekday = (DayOfWeek)Array.IndexOf(r.NotesPerWeekday, max);
        }
    }

    private static void ComputeBible(Database db, StudyStatsResult r, Dictionary<int, Location> locations)
    {
        var perBook = new Dictionary<int, int>();
        var chapters = new HashSet<(int, int)>();

        void Touch(int? locationId)
        {
            if (locationId == null ||
                !locations.TryGetValue(locationId.Value, out var loc) ||
                loc.BookNumber == null ||
                loc.BookNumber < 1 || loc.BookNumber > BibleBookCount)
            {
                return;
            }

            perBook[loc.BookNumber.Value] = perBook.TryGetValue(loc.BookNumber.Value, out var c) ? c + 1 : 1;
            if (loc.ChapterNumber != null)
            {
                chapters.Add((loc.BookNumber.Value, loc.ChapterNumber.Value));
            }
        }

        foreach (var n in db.Notes) Touch(n.LocationId);
        foreach (var u in db.UserMarks) Touch(u.LocationId);
        foreach (var b in db.Bookmarks) Touch(b.LocationId);

        r.BibleBooksTouched = perBook.Count;
        r.BibleChaptersTouched = chapters.Count;

        foreach (var kv in perBook.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Take(5))
        {
            r.TopBibleBooks.Add(new NamedCount(BibleBookNames.GetName(kv.Key), kv.Value));
        }
    }

    private static void ComputeHighlightedChapters(Database db, StudyStatsResult r, Dictionary<int, Location> locations)
    {
        var perChapter = new Dictionary<(int Book, int Chapter), int>();
        foreach (var u in db.UserMarks)
        {
            if (locations.TryGetValue(u.LocationId, out var loc) &&
                loc.BookNumber is >= 1 and <= BibleBookCount &&
                loc.ChapterNumber != null)
            {
                var key = (loc.BookNumber.Value, loc.ChapterNumber.Value);
                perChapter[key] = perChapter.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }

        foreach (var kv in perChapter.OrderByDescending(k => k.Value).ThenBy(k => k.Key.Book).ThenBy(k => k.Key.Chapter).Take(5))
        {
            r.TopHighlightedChapters.Add(new NamedCount(BibleBookNames.GetName(kv.Key.Book) + " " + kv.Key.Chapter, kv.Value));
        }
    }

    private static void ComputeTags(Database db, StudyStatsResult r)
    {
        var names = new Dictionary<int, string>();
        foreach (var t in db.Tags)
        {
            if (t.Type == 1)
            {
                names[t.TagId] = t.Name;
            }
        }

        var counts = new Dictionary<int, int>();
        foreach (var m in db.TagMaps)
        {
            if (m.NoteId != null && names.ContainsKey(m.TagId))
            {
                counts[m.TagId] = counts.TryGetValue(m.TagId, out var c) ? c + 1 : 1;
            }
        }

        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => names[k.Key], StringComparer.OrdinalIgnoreCase).Take(5))
        {
            r.TopTags.Add(new NamedCount(names[kv.Key], kv.Value));
        }
    }

    private static void ComputePublications(Database db, StudyStatsResult r, Dictionary<int, Location> locations)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var n in db.Notes)
        {
            if (n.LocationId != null &&
                locations.TryGetValue(n.LocationId.Value, out var loc) &&
                loc.BookNumber == null && // Bible notes are covered by the Bible section
                !string.IsNullOrWhiteSpace(loc.KeySymbol))
            {
                var key = loc.KeySymbol!;
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }

        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Take(5))
        {
            r.TopPublications.Add(new NamedCount(kv.Key, kv.Value));
        }
    }

    private static void ComputeMilestones(StudyStatsResult r)
    {
        // Only the highest level reached per category. Keys are resolved to localized text by the caller.
        void Highest(string category, int value, params int[] levels)
        {
            var reached = levels.Where(l => value >= l).ToList();
            if (reached.Count > 0)
            {
                r.Milestones.Add(category + ":" + reached.Max());
            }
        }

        Highest("notes", r.Notes, 1, 10, 50, 100, 250, 500, 1000, 2500, 5000);
        Highest("highlights", r.Highlights, 10, 100, 500, 1000);
        Highest("streak", r.LongestStreak, 3, 7, 14, 30, 100);
        Highest("books", r.BibleBooksTouched, 10, 33, 66);
        Highest("tags", r.Tags, 5, 20);
    }
}
