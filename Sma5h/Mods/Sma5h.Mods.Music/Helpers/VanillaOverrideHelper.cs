using Microsoft.Extensions.Logging;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System.Collections.Generic;
using System.Linq;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>Applies tracks.csv edits of vanilla songs and series to the loaded game data.</summary>
    public static class VanillaOverrideHelper
    {
        /// <summary>The game locale the edited (English) texts are compared against.</summary>
        private const string VanillaLocale = "us_en";

        public static void Apply(IAudioStateService state, IEnumerable<VanillaBgmOverride> overrides, string defaultLocale, ILogger logger)
        {
            var dbRoots = state.GetBgmDbRootEntries().Where(e => e.Source == EntrySource.Core).ToDictionary(e => e.UiBgmId);
            var streamSets = state.GetBgmStreamSetEntries().ToDictionary(s => s.StreamSetId);
            foreach (var edit in overrides)
            {
                if (!dbRoots.TryGetValue(edit.UiBgmId, out var entry))
                {
                    logger.LogWarning("Vanilla song {UiBgmId} not found in the game data; its edits are skipped.", edit.UiBgmId);
                    continue;
                }

                SetText(entry.Title, edit.Title, defaultLocale);
                SetText(entry.Author, edit.Author, defaultLocale);
                SetText(entry.Copyright, edit.Copyright, defaultLocale);
                entry.RecordType = edit.RecordType;
                entry.UiGameTitleId = edit.UiGameTitleId;
                if (!edit.InSoundtest)
                    entry.TestDispOrder = -1;
                if (entry.StreamSetId != null && streamSets.TryGetValue(entry.StreamSetId, out var streamSet))
                {
                    streamSet.SpecialCategory = edit.SpecialCategory;
                    streamSet.Info1 = edit.Info1 ?? "";
                }
            }
        }

        /// <summary>Sets the icon.png of each vanilla series that has one.</summary>
        public static void ApplySeriesIcons(IAudioStateService state, IReadOnlyDictionary<string, string> iconsBySeriesId)
        {
            foreach (var series in state.GetSeriesEntries().Where(s => iconsBySeriesId.ContainsKey(s.UiSeriesId)))
                series.IconPath = iconsBySeriesId[series.UiSeriesId];
        }

        // An unchanged text keeps every translation; a changed one replaces them all.
        private static void SetText(Dictionary<string, string> text, string value, string defaultLocale)
        {
            if (string.IsNullOrEmpty(value) || text.GetValueOrDefault(VanillaLocale) == value) return;
            text.Clear();
            text[defaultLocale] = value;
        }
    }
}
