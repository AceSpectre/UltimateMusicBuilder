using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sma5h.Mods.Music.Models
{
    public class VanillaSongOverride
    {
        public const string FileName = "vanilla_tracks.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public string BgmId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Copyright { get; set; }
        public float? Volume { get; set; }
        public string Info1 { get; set; }
        public string SpecialCategory { get; set; }
        [JsonIgnore] public bool HasChanges => Title != null || Author != null || Copyright != null ||
            Volume != null || Info1 != null || SpecialCategory != null;

        public static List<VanillaSongOverride> Read(string seriesPath)
        {
            var path = Path.Combine(seriesPath, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<List<VanillaSongOverride>>(File.ReadAllText(path), JsonOptions) ?? new() : new();
        }

        public static void Write(string seriesPath, IEnumerable<VanillaSongOverride> overrides) =>
            File.WriteAllText(Path.Combine(seriesPath, FileName), JsonSerializer.Serialize(overrides.Where(o => o.HasChanges), JsonOptions) + "\n");

        public void Apply(BgmDbRootEntry root, BgmStreamSetEntry set)
        {
            if (Volume is float volume && (!float.IsFinite(volume) || volume < 0))
                throw new InvalidDataException("Vanilla song volume must be a finite non-negative multiplier.");
            SetText(root.Title, Title);
            SetText(root.Author, Author);
            SetText(root.Copyright, Copyright);
            if (set != null)
            {
                if (Info1 != null) set.Info1 = Info1;
                if (SpecialCategory != null) set.SpecialCategory = SpecialCategory;
            }
        }

        private static void SetText(Dictionary<string, string> text, string value)
        {
            if (value == null) return;
            foreach (var locale in text.Keys.ToList()) text[locale] = value;
            text["us_en"] = value;
        }
    }
}
