using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>dB boosts for vanilla songs.</summary>
    public static class VanillaVolumeHelper
    {
        /// <summary>
        /// Each vanilla song's global + series + track boost (track by UiBgmId), keyed by tone id.
        /// Songs with no boost are left out so their vanilla nus3bank is kept. Empty unless
        /// <paramref name="enabled"/> (experimental ApplyVolumeToVanilla).
        /// </summary>
        public static Dictionary<string, float> BoostsByToneId(IAudioStateService state, float globalDb, IReadOnlyDictionary<string, float> seriesDb,
            IReadOnlyDictionary<string, float> trackDb, bool enabled)
        {
            if (!enabled)
                return new Dictionary<string, float>();

            var seriesByGameTitle = FirstBy(state.GetGameTitleEntries(), g => g.UiGameTitleId, g => g.UiSeriesId);
            var gameTitleByBgm = FirstBy(state.GetBgmDbRootEntries(), d => d.UiBgmId, d => d.UiGameTitleId);
            var bgmBySet = FirstBy(state.GetBgmDbRootEntries(), d => d.StreamSetId, d => d.UiBgmId);
            var setByInfo = FirstBy(state.GetBgmStreamSetEntries(), s => s.Info0, s => s.StreamSetId);
            var infoByStream = FirstBy(state.GetBgmAssignedInfoEntries(), a => a.StreamId, a => a.InfoId);
            var streamByTone = FirstBy(state.GetBgmStreamPropertyEntries(), p => p.DataName0, p => p.StreamId);

            string BgmId(string toneId) =>
                streamByTone.TryGetValue(toneId, out var stream) && infoByStream.TryGetValue(stream, out var info)
                && setByInfo.TryGetValue(info, out var set) && bgmBySet.TryGetValue(set, out var bgm) ? bgm : null;

            float SeriesDb(string bgmId) =>
                bgmId != null && gameTitleByBgm.TryGetValue(bgmId, out var title) && title != null
                && seriesByGameTitle.TryGetValue(title, out var series) && series != null && seriesDb.TryGetValue(series, out var db) ? db : 0f;

            float Boost(string bgmId) =>
                globalDb + SeriesDb(bgmId) + (bgmId != null ? trackDb.GetValueOrDefault(bgmId) : 0f);

            return state.GetBgmPropertyEntries()
                .Where(p => p.Source != EntrySource.Mod)
                .Select(p => (p.NameId, Db: Boost(BgmId(p.NameId))))
                .Where(b => b.Db != 0)
                .ToDictionary(b => b.NameId, b => b.Db);
        }

        private static Dictionary<string, string> FirstBy<T>(IEnumerable<T> items, Func<T, string> key, Func<T, string> value) =>
            items.Where(i => key(i) != null).GroupBy(key).ToDictionary(g => g.Key, g => value(g.First()));
    }
}
