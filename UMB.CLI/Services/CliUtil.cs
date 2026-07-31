using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Tomlyn;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Shared helpers for the CLI services (string/TOML/CSV/JSON plumbing that was
    /// previously duplicated per service).
    /// </summary>
    public static class CliUtil
    {
        public static readonly HashSet<string> SourceAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg"
        };

        public const string ValidateFolder = "songs-to-validate";

        public static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

        public static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static TomlModelOptions KebabTomlOptions() => new() { ConvertPropertyName = ToKebabCase };

        public static string EscapeToml(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

        /// <summary>
        /// Emits the [series] table (field order fixed: id, name, existing-series,
        /// playlist-incidence, series-playlist) followed by a blank line. Null/empty
        /// optional values are omitted.
        /// </summary>
        public static void AppendSeriesHeader(StringBuilder sb, string id, string name,
            bool existingSeries = false, int? playlistIncidence = null, string seriesPlaylist = null)
        {
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeToml(id)}\"");
            sb.AppendLine($"name = \"{EscapeToml(name)}\"");
            if (existingSeries)
                sb.AppendLine("existing-series = true");
            if (playlistIncidence.HasValue)
                sb.AppendLine($"playlist-incidence = {playlistIncidence.Value}");
            if (!string.IsNullOrEmpty(seriesPlaylist))
                sb.AppendLine($"series-playlist = \"{EscapeToml(seriesPlaylist)}\"");
            sb.AppendLine();
        }

        /// <summary>Emits one [[games]] block (id + name) followed by a blank line.</summary>
        public static void AppendGameBlock(StringBuilder sb, string id, string name)
        {
            sb.AppendLine("[[games]]");
            sb.AppendLine($"id = \"{EscapeToml(id)}\"");
            sb.AppendLine($"name = \"{EscapeToml(name)}\"");
            sb.AppendLine();
        }

        public static string MakeSafeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(InvalidFileNameChars.Contains(c) ? '_' : c);
            return sb.ToString();
        }

        public static string SanitizeFolderName(string name)
        {
            return MakeSafeFileName(name).Trim().ToLowerInvariant().Replace(' ', '-');
        }

        public static CsvConfiguration CsvRead() => new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null
        };

        public static CsvConfiguration CsvReadLenient() => new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        public static CsvConfiguration CsvWrite() => new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
    }
}
