using System;
using System.Collections.Generic;
using System.Linq;

namespace JWLMergeBot
{
    /// <summary>
    /// Draws small text bar charts. Meant for a monospace block (Telegram's &lt;pre&gt;),
    /// where every column lines up whatever the length of the names.
    /// </summary>
    public static class TextBars
    {
        private static readonly string[] Partial = { "", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };

        /// <summary>A bar of up to <paramref name="width"/> cells, in steps of 1/8 of a cell.</summary>
        public static string Bar(int value, int max, int width = 10)
        {
            if (max <= 0 || value <= 0)
                return "";

            int eighths = Math.Max(1, (int)Math.Round((double)value / max * width * 8));
            return new string('█', eighths / 8) + Partial[eighths % 8];
        }

        /// <summary>One line per item: name, bar and count, in aligned columns.</summary>
        public static string Table(IEnumerable<KeyValuePair<string, int>> items, int maxNameWidth = 14, int barWidth = 10)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return "";

            int max = list.Max(i => i.Value);
            int nameWidth = Math.Min(maxNameWidth, list.Max(i => i.Key.Length));
            int countWidth = list.Max(i => i.Value.ToString().Length);

            return string.Join("\n", list.Select(i =>
                Shorten(i.Key, nameWidth).PadRight(nameWidth) + " " +
                Bar(i.Value, max, barWidth).PadRight(barWidth) + " " +
                i.Value.ToString().PadLeft(countWidth)));
        }

        private static string Shorten(string text, int width)
        {
            if (text.Length <= width)
                return text;

            int cut = width - 1;
            if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) // don't split an emoji in half
                cut--;
            return text.Substring(0, cut) + "…";
        }
    }
}
