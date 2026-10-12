using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>dB boosts for vanilla songs when volume settings are applied to them (experimental).</summary>
    public static class VanillaVolumeHelper
    {
        /// <summary>
        /// The global boost plus the boost of each vanilla song's series, keyed by tone id.
        /// Songs with no boost are left out so their vanilla nus3bank is kept.
        /// </summary>
        public static Dictionary<string, float> BoostsByToneId(IAudioStateService state, float globalDb, IReadOnlyDictionary<string, float> seriesDb)
        {
            var seriesByGameTitle = FirstBy(state.GetGameTitleEntries(), g => g.UiGameTitleId, g => g.UiSeriesId);
            var gameTitleBySet = FirstBy(state.GetBgmDbRootEntries(), d => d.StreamSetId, d => d.UiGameTitleId);
            var setByInfo = FirstBy(state.GetBgmStreamSetEntries(), s => s.Info0, s => s.StreamSetId);
            var infoByStream = FirstBy(state.GetBgmAssignedInfoEntries(), a => a.StreamId, a => a.InfoId);
            var streamByTone = FirstBy(state.GetBgmStreamPropertyEntries(), p => p.DataName0, p => p.StreamId);

            float SeriesDb(string toneId) =>
                streamByTone.TryGetValue(toneId, out var stream) && infoByStream.TryGetValue(stream, out var info)
                && setByInfo.TryGetValue(info, out var set) && gameTitleBySet.TryGetValue(set, out var title)
                && title != null && seriesByGameTitle.TryGetValue(title, out var series)
                && series != null && seriesDb.TryGetValue(series, out var db) ? db : 0f;

            return state.GetBgmPropertyEntries()
                .Where(p => p.Source != EntrySource.Mod)
                .Select(p => (p.NameId, Db: globalDb + SeriesDb(p.NameId)))
                .Where(b => b.Db != 0)
                .ToDictionary(b => b.NameId, b => b.Db);
        }

        private static Dictionary<string, string> FirstBy<T>(IEnumerable<T> items, Func<T, string> key, Func<T, string> value) =>
            items.Where(i => key(i) != null).GroupBy(key).ToDictionary(g => g.Key, g => value(g.First()));
    }
}
