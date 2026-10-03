using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Line/regex helpers for the hand-edited series.toml and *-order.toml files. They preserve
    /// comments and unknown keys, which a TOML model round-trip would drop.
    /// </summary>
    public static class SeriesToml
    {
        private static readonly Regex TableHeaderLine = new(@"^\s*\[", RegexOptions.Compiled);

        public static string Escape(string value) => CliUtil.EscapeToml(value);

        /// <summary>The body of a [header] table, up to the next table header; null when absent.</summary>
        public static string TableSection(string text, string header)
        {
            var parts = Regex.Split(text, $@"^\s*\[{Regex.Escape(header)}\]\s*$", RegexOptions.Multiline);
            return parts.Length < 2 ? null : Regex.Split(parts[1], @"^\s*\[", RegexOptions.Multiline)[0];
        }

        /// <summary>The body of each [[name]] array-of-tables block.</summary>
        public static List<string> TableArrayBlocks(string text, string name) =>
            Regex.Split(text, $@"^\s*\[\[{Regex.Escape(name)}\]\]\s*$", RegexOptions.Multiline)
                .Skip(1)
                .Select(block => Regex.Split(block, @"^\s*\[", RegexOptions.Multiline)[0])
                .ToList();

        /// <summary>Reads <c>key = "value"</c> from a table body ("" when absent).</summary>
        public static string String(string section, string key)
        {
            var match = Regex.Match(section ?? "", $@"^\s*{Regex.Escape(key)}\s*=\s*""([^""]*)""", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : "";
        }

        /// <summary>Like <see cref="String"/> but null when the key is absent or empty.</summary>
        public static string NonEmptyString(string section, string key) =>
            String(section, key) is { Length: > 0 } value ? value : null;

        public record SeriesHeader(string Id, string Name, bool ExistingSeries);

        /// <summary>The [series] id, name and existing-series flag (the whole file when there is no [series] header).</summary>
        public static SeriesHeader ReadHeader(string text)
        {
            var series = TableSection(text, "series") ?? text;
            var existing = Regex.Match(series, @"^\s*existing-series\s*=\s*(true|false)", RegexOptions.Multiline);
            return new SeriesHeader(NonEmptyString(series, "id"), NonEmptyString(series, "name"),
                existing.Success && existing.Groups[1].Value == "true");
        }

        public record TrackDefaults(string Game, string Author, string Copyright, string RecordType, double Volume);

        /// <summary>The [default-track-data] table, or null when absent.</summary>
        public static TrackDefaults ReadDefaults(string text)
        {
            var section = TableSection(text, "default-track-data");
            if (section == null) return null;
            var volume = Regex.Match(section, @"^\s*volume\s*=\s*([0-9.]+)", RegexOptions.Multiline);
            return new TrackDefaults(String(section, "game"), String(section, "author"), String(section, "copyright"),
                CliUtil.FirstNonEmpty(String(section, "record-type"), "original"),
                volume.Success && double.TryParse(volume.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1);
        }

        /// <summary>The [[games]] blocks (name defaults to id).</summary>
        public static List<SeriesGame> Games(string text)
        {
            var games = new List<SeriesGame>();
            foreach (var block in TableArrayBlocks(text, "games"))
            {
                var id = NonEmptyString(block, "id");
                if (id == null) continue;
                var name = Regex.Match(block, @"^\s*name\s*=\s*""([^""]*)""", RegexOptions.Multiline);
                games.Add(new SeriesGame(id, name.Success ? name.Groups[1].Value : id));
            }
            return games;
        }

        /// <summary>Every quoted string in an id-list file (song_order.toml / series-order.toml).</summary>
        public static List<string> ReadIdList(string path)
        {
            if (!File.Exists(path))
                return new List<string>();
            return Regex.Matches(File.ReadAllText(path), @"""([^""]+)""")
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        public static string[] ReadLines(string path) => Regex.Split(File.ReadAllText(path), @"\r?\n");

        public static void WriteLines(string path, IEnumerable<string> lines) =>
            File.WriteAllText(path, string.Join("\n", lines));

        /// <summary>A key to upsert in a table; a null line removes the key.</summary>
        public record Entry(string Key, string Line);

        /// <summary>
        /// Upserts keys in a [header] table, keeping unmanaged keys, comments and other tables.
        /// A missing table is appended only when createIfMissing and there is something to add.
        /// </summary>
        public static List<string> UpsertTable(IReadOnlyList<string> lines, string header, IReadOnlyList<Entry> entries, bool createIfMissing)
        {
            var headerIndex = IndexOfLine(lines, $"[{header}]");
            if (headerIndex < 0)
            {
                var additions = entries.Where(e => e.Line != null).Select(e => e.Line).ToList();
                if (!createIfMissing || additions.Count == 0) return lines.ToList();
                var trimmed = TrimTrailingBlankLines(lines);
                trimmed.Add("");
                trimmed.Add($"[{header}]");
                trimmed.AddRange(additions);
                return trimmed;
            }

            var endIndex = lines.Count;
            for (var i = headerIndex + 1; i < lines.Count; i++)
            {
                if (TableHeaderLine.IsMatch(lines[i]))
                {
                    endIndex = i;
                    break;
                }
            }

            var byKey = entries.ToDictionary(e => e.Key, e => e.Line);
            var handled = new HashSet<string>();
            var body = new List<string>();
            for (var i = headerIndex + 1; i < endIndex; i++)
            {
                var key = Regex.Match(lines[i], @"^\s*([A-Za-z0-9_-]+)\s*=");
                if (key.Success && byKey.TryGetValue(key.Groups[1].Value, out var line))
                {
                    handled.Add(key.Groups[1].Value);
                    if (line != null) body.Add(line);
                }
                else
                {
                    body.Add(lines[i]);
                }
            }
            body.AddRange(entries.Where(e => !handled.Contains(e.Key) && e.Line != null).Select(e => e.Line));

            return lines.Take(headerIndex + 1).Concat(body).Concat(lines.Skip(endIndex)).ToList();
        }

        /// <summary>
        /// Removes every [[name]] block. Returns the remaining lines and the index the first
        /// removed block started at (-1 when there were none).
        /// </summary>
        public static (List<string> kept, int insertIndex) StripArrayTables(IReadOnlyList<string> lines, string name)
        {
            var kept = new List<string>();
            var insertIndex = -1;
            var i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Trim() == $"[[{name}]]")
                {
                    if (insertIndex < 0) insertIndex = kept.Count;
                    i++;
                    while (i < lines.Count && !TableHeaderLine.IsMatch(lines[i])) i++;
                }
                else
                {
                    kept.Add(lines[i]);
                    i++;
                }
            }
            return (kept, insertIndex);
        }

        /// <summary>
        /// Replaces all [[games]] blocks, in place; when there were none they go right after the
        /// [series] table.
        /// </summary>
        public static List<string> RewriteGames(IReadOnlyList<string> lines, IReadOnlyList<SeriesGame> games)
        {
            var (kept, insertIndex) = StripArrayTables(lines, "games");
            if (insertIndex < 0)
            {
                insertIndex = kept.Count;
                var seriesHeader = IndexOfLine(kept, "[series]");
                if (seriesHeader >= 0)
                {
                    for (var j = seriesHeader + 1; j < kept.Count; j++)
                    {
                        if (TableHeaderLine.IsMatch(kept[j]))
                        {
                            insertIndex = j;
                            break;
                        }
                    }
                }
            }

            var block = new List<string>();
            foreach (var game in games)
                block.AddRange(new[] { "[[games]]", $"id = \"{Escape(game.Id)}\"", $"name = \"{Escape(game.Name)}\"", "" });
            if (block.Count > 0) block.RemoveAt(block.Count - 1);

            var result = kept.Take(insertIndex).ToList();
            var after = kept.Skip(insertIndex).ToList();
            if (block.Count > 0)
            {
                if (result.Count > 0 && result[^1].Trim() != "") result.Add("");
                result.AddRange(block);
                if (after.Count > 0 && after[0].Trim() != "") result.Add("");
            }
            result.AddRange(after);
            return result;
        }

        /// <summary>
        /// Replaces all [[playlists]] blocks with <paramref name="blocks"/>, in place; when there
        /// were none they are appended to the end of the file.
        /// </summary>
        public static List<string> RewritePlaylists(IReadOnlyList<string> lines, IReadOnlyList<List<string>> blocks)
        {
            var flat = new List<string>();
            for (var b = 0; b < blocks.Count; b++)
            {
                if (b > 0) flat.Add("");
                flat.AddRange(blocks[b]);
            }

            var (kept, insertIndex) = StripArrayTables(lines, "playlists");
            if (flat.Count == 0) return kept;
            if (insertIndex < 0)
            {
                var trimmed = TrimTrailingBlankLines(kept);
                trimmed.Add("");
                trimmed.AddRange(flat);
                trimmed.Add("");
                return trimmed;
            }
            return kept.Take(insertIndex).Concat(flat).Append("").Concat(kept.Skip(insertIndex)).ToList();
        }

        private static int IndexOfLine(IReadOnlyList<string> lines, string trimmedLine)
        {
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].Trim() == trimmedLine) return i;
            return -1;
        }

        private static List<string> TrimTrailingBlankLines(IEnumerable<string> lines)
        {
            var trimmed = lines.ToList();
            while (trimmed.Count > 0 && trimmed[^1].Trim() == "") trimmed.RemoveAt(trimmed.Count - 1);
            return trimmed;
        }
    }
}
