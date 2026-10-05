using System;
using System.Collections.Generic;
using System.Linq;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public sealed class NoteHit
{
    public NoteHit(string where, string title, string snippet, DateTime modified, IReadOnlyList<string> tags)
    {
        Where = where;
        Title = title;
        Snippet = snippet;
        Modified = modified;
        Tags = tags;
    }

    /// <summary>Place of the note, e.g. "John 3:16". Empty if unknown.</summary>
    public string Where { get; }

    public string Title { get; }
    public string Snippet { get; }
    public DateTime Modified { get; }
    public IReadOnlyList<string> Tags { get; }
}

public sealed class NoteSearchResult
{
    /// <summary>All matching notes, also those not listed in <see cref="Hits"/>.</summary>
    public int Total { get; set; }

    public List<NoteHit> Hits { get; } = new();
}

/// <summary>
/// Finds notes by words. Every word of the query must appear (in any order) in the title, the text,
/// the tags or the place of a note. Upper and lower case do not matter. Read only.
/// </summary>
public sealed class NoteSearch
{
    private const int SnippetRadius = 70;

    public NoteSearchResult Search(BackupFile backup, string query, int maxHits = 8)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        var result = new NoteSearchResult();
        var words = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count == 0)
        {
            return result;
        }

        var db = backup.Database;
        var locations = new Dictionary<int, Location>();
        foreach (var l in db.Locations) locations[l.LocationId] = l;

        var tagNames = new Dictionary<int, string>();
        foreach (var t in db.Tags) if (t.Type == 1) tagNames[t.TagId] = t.Name;

        var tagsOfNote = new Dictionary<int, List<string>>();
        foreach (var m in db.TagMaps)
        {
            if (m.NoteId != null && tagNames.TryGetValue(m.TagId, out var name))
            {
                if (!tagsOfNote.TryGetValue(m.NoteId.Value, out var list))
                {
                    tagsOfNote[m.NoteId.Value] = list = new List<string>();
                }

                list.Add(name);
            }
        }

        var hits = new List<NoteHit>();
        foreach (var n in db.Notes)
        {
            var where = LocationDescriber.Describe(n, locations) ?? string.Empty;
            var tags = tagsOfNote.TryGetValue(n.NoteId, out var tl) ? tl : new List<string>();
            var title = n.Title ?? string.Empty;
            var content = n.Content ?? string.Empty;
            var haystack = title + "\n" + content + "\n" + string.Join("\n", tags) + "\n" + where;

            if (!words.All(w => haystack.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            hits.Add(new NoteHit(where, title.Trim(), MakeSnippet(title, content, words), n.GetLastModifiedDateTime(), tags));
        }

        result.Total = hits.Count;
        result.Hits.AddRange(hits.OrderByDescending(h => h.Modified).Take(Math.Max(1, maxHits)));
        return result;
    }

    private static string MakeSnippet(string title, string content, List<string> words)
    {
        // show the text around the first word found in the note text; otherwise the start of the text
        var text = Collapse(content);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var pos = -1;
        foreach (var w in words)
        {
            var i = text.IndexOf(w, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (pos < 0 || i < pos))
            {
                pos = i;
            }
        }

        if (pos < 0)
        {
            pos = 0;
        }

        var start = Math.Max(0, pos - SnippetRadius);
        var end = Math.Min(text.Length, pos + SnippetRadius * 2);
        var snippet = text.Substring(start, end - start).Trim();
        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }

    private static string Collapse(string s)
    {
        return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
