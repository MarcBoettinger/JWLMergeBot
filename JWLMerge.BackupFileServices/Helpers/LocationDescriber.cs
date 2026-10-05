using System.Collections.Generic;
using JWLMerge.BackupFileServices.Models.DatabaseModels;

namespace JWLMerge.BackupFileServices.Helpers;

/// <summary>Human readable place of a note, e.g. "John 3:16" or "mwb 20250100".</summary>
internal static class LocationDescriber
{
    public static string? Describe(Note n, Dictionary<int, Location> locations)
    {
        if (n.LocationId == null || !locations.TryGetValue(n.LocationId.Value, out var loc))
        {
            return null;
        }

        return Describe(loc, n.BlockType == 2 ? n.BlockIdentifier : null);
    }

    public static string? Describe(Location loc, int? verse)
    {
        if (loc.BookNumber is >= 1 and <= 66)
        {
            var text = BibleBookNames.GetName(loc.BookNumber.Value);
            if (loc.ChapterNumber != null)
            {
                text += " " + loc.ChapterNumber;
                if (verse != null)
                {
                    text += ":" + verse;
                }
            }

            return text;
        }

        if (!string.IsNullOrWhiteSpace(loc.KeySymbol))
        {
            return loc.IssueTagNumber > 0 ? $"{loc.KeySymbol} {loc.IssueTagNumber}" : loc.KeySymbol;
        }

        return null;
    }
}
