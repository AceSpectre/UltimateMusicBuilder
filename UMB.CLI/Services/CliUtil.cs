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
