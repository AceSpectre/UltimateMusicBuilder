using CsvHelper;
using CsvHelper.Configuration;
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
                ShouldQuote = args => args.Field != null && args.Field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
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

        /// <summary>Non-empty lines after the header; 0 when the file is missing or unreadable.</summary>
        public static int CountDataRows(string csvPath)
        {
            try { return Math.Max(0, File.ReadLines(csvPath).Count(l => l.Trim().Length > 0) - 1); }
            catch (IOException) { return 0; }
        }

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
