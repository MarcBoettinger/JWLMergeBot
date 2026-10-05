using System;
using System.Collections.Generic;
using System.Linq;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

public sealed class TagInfo
{
    public TagInfo(int tagId, string name, int items)
    {
        TagId = tagId;
        Name = name;
        Items = items;
    }

    public int TagId { get; }
    public string Name { get; }

    /// <summary>How many things carry this tag (normally notes).</summary>
    public int Items { get; }
}

public enum TagResult
{
    Ok,
    NotFound,
    Ambiguous,
    NameTaken,
    Same,
    Invalid,
}

/// <summary>Lists, renames, merges and cleans up the user's own tags (type 1). Favourites are never touched.</summary>
public sealed class TagManager
{
    private const int UserTagType = 1;
    private const int MaxNameLength = 100;

    public List<TagInfo> List(Database db)
    {
        var counts = db.TagMaps.GroupBy(m => m.TagId).ToDictionary(g => g.Key, g => g.Count());
        return db.Tags
            .Where(t => t.Type == UserTagType)
            .Select(t => new TagInfo(t.TagId, t.Name, counts.TryGetValue(t.TagId, out var c) ? c : 0))
            .OrderByDescending(t => t.Items)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int CountUnused(Database db)
    {
        var used = new HashSet<int>(db.TagMaps.Select(m => m.TagId));
        return db.Tags.Count(t => t.Type == UserTagType && !used.Contains(t.TagId));
    }

    public int DeleteUnused(Database db)
    {
        var used = new HashSet<int>(db.TagMaps.Select(m => m.TagId));
        return db.Tags.RemoveAll(t => t.Type == UserTagType && !used.Contains(t.TagId));
    }

    public TagResult Rename(Database db, string oldName, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (newName.Length == 0 || newName.Length > MaxNameLength)
        {
            return TagResult.Invalid;
        }

        var found = Find(db, oldName, out var tag);
        if (found != TagResult.Ok)
        {
            return found;
        }

        if (string.Equals(tag!.Name, newName, StringComparison.Ordinal))
        {
            return TagResult.Same;
        }

        // the database has a unique index on (type, name): an existing exact name must be merged instead
        if (db.Tags.Any(t => t.Type == UserTagType && t.TagId != tag.TagId && string.Equals(t.Name, newName, StringComparison.Ordinal)))
        {
            return TagResult.NameTaken;
        }

        tag.Name = newName;
        return TagResult.Ok;
    }

    /// <summary>Moves everything tagged with <paramref name="sourceName"/> to <paramref name="targetName"/> and removes the source tag.</summary>
    public TagResult Merge(Database db, string sourceName, string targetName, out int moved)
    {
        moved = 0;

        var r = Find(db, sourceName, out var source);
        if (r != TagResult.Ok)
        {
            return r;
        }

        r = Find(db, targetName, out var target);
        if (r != TagResult.Ok)
        {
            return r;
        }

        if (source!.TagId == target!.TagId)
        {
            return TagResult.Same;
        }

        string Key(TagMap m) => $"{m.NoteId}|{m.LocationId}|{m.PlaylistItemId}";
        var already = new HashSet<string>(db.TagMaps.Where(m => m.TagId == target.TagId).Select(Key));
        var nextPosition = db.TagMaps.Where(m => m.TagId == target.TagId).Select(m => m.Position + 1).DefaultIfEmpty(0).Max();

        foreach (var m in db.TagMaps.Where(m => m.TagId == source.TagId).OrderBy(m => m.Position).ToList())
        {
            if (already.Add(Key(m)))
            {
                m.TagId = target.TagId;
                m.Position = nextPosition++; // unique constraint on (tag, position)
                moved++;
            }
            else
            {
                db.TagMaps.Remove(m); // the target tag was already on that item
            }
        }

        db.Tags.Remove(source);
        return TagResult.Ok;
    }

    /// <summary>Finds a user tag by name: exact match first, otherwise a unique match ignoring upper/lower case.</summary>
    public TagResult Find(Database db, string name, out Tag? tag)
    {
        tag = null;
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return TagResult.NotFound;
        }

        var userTags = db.Tags.Where(t => t.Type == UserTagType).ToList();

        var exact = userTags.Where(t => string.Equals(t.Name, name, StringComparison.Ordinal)).ToList();
        if (exact.Count == 1)
        {
            tag = exact[0];
            return TagResult.Ok;
        }

        var loose = userTags.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (loose.Count == 1)
        {
            tag = loose[0];
            return TagResult.Ok;
        }

        return loose.Count > 1 ? TagResult.Ambiguous : TagResult.NotFound;
    }
}
