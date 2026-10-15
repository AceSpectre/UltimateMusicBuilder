using CsvHelper;
using CsvHelper.Configuration;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace UMB.CLI.Services
{
    /// <summary>A tracks.csv row keyed by header (case-insensitive).</summary>
    public class CsvRow : Dictionary<string, string>
    {
        public CsvRow() : base(StringComparer.OrdinalIgnoreCase) { }

        public string Get(string column) => TryGetValue(column, out var value) ? value ?? "" : "";
    }

    /// <summary>Reads and writes tracks.csv with every column and cell preserved.</summary>
    public static class TracksCsv
    {
        /// <summary>The column holding each track's dB boost.</summary>
        public const string VolumeDbColumn = "volume_db";

        /// <summary>The standard columns, in order.</summary>
        public static readonly string[] Columns =
            { "filename", "game", "title", "author", "copyright", "record_type", "special_category", VolumeDbColumn, "info1", "in_soundtest" };

        /// <summary>Header line of a new, empty tracks.csv.</summary>
        public static string Header => string.Join(",", Columns) + "\n";

        private static readonly char[] CharsNeedingQuotes = { ',', '"', '\r', '\n' };

        /// <summary>Untrimmed read for lossless round-trips, with the header order.</summary>
        public static (List<CsvRow> rows, string[] headers) Read(string csvPath) => Read(csvPath, TrimOptions.None);

        /// <summary>Trimmed read for display/merging; empty when the file is missing or unreadable.</summary>
        public static List<CsvRow> ReadLenient(string csvPath)
        {
            if (!File.Exists(csvPath)) return new List<CsvRow>();
            try { return Read(csvPath, TrimOptions.Trim).rows; }
            catch (Exception ex) when (ex is CsvHelperException or IOException) { return new List<CsvRow>(); }
        }

        public static void Write(string csvPath, IEnumerable<CsvRow> rows, IReadOnlyList<string> headers)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n",
                ShouldQuote = args => args.Field != null && args.Field.IndexOfAny(CharsNeedingQuotes) >= 0
            };
            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, config);
            foreach (var header in headers)
                csv.WriteField(header);
            csv.NextRecord();
            foreach (var row in rows)
            {
                foreach (var header in headers)
                    csv.WriteField(row.Get(header));
                csv.NextRecord();
            }
        }

        /// <summary>A row whose audio file is not in the series folder; vanilla song edits have no file.</summary>
        public static bool IsMissingFile(string seriesDir, string filename) =>
            !FolderMusicMod.IsVanillaBgmId(filename) && !File.Exists(Path.Combine(seriesDir, filename));

        /// <summary>Rows other than vanilla song edits; 0 when the file is missing or unreadable.</summary>
        public static int CountDataRows(string csvPath) =>
            ReadLenient(csvPath).Count(r => !FolderMusicMod.IsVanillaBgmId(r.Get("filename")));

        private static (List<CsvRow> rows, string[] headers) Read(string csvPath, TrimOptions trim)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = trim,
                MissingFieldFound = null,
                BadDataFound = null,
                HeaderValidated = null
            };
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, config);
            if (!csv.Read())
                return (new List<CsvRow>(), Array.Empty<string>());
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            var rows = new List<CsvRow>();
            while (csv.Read())
            {
                var row = new CsvRow();
                for (var i = 0; i < headers.Length; i++)
                    row[headers[i]] = csv.TryGetField<string>(i, out var value) ? value ?? "" : "";
                rows.Add(row);
            }
            return (rows, headers);
        }
    }
}
