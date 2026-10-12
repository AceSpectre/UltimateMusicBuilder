using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Data;
using Sma5h.Data.Ui.Param.Database;
using Sma5h.Data.Ui.Param.Database.PrcUiBgmDatabaseModels;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Services;
using Sma5h.ResourceProviders;
using Sma5h.ResourceProviders.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Read-only vanilla game data (song/game titles, songs, stage playlists) from the extracted
    /// game files, cached per game resources folder.
    /// </summary>
    public class VanillaCatalogService
    {
        public record GameTitle(string Id, string Name, string SeriesId);
        /// <summary>A sound test series; Id has no ui_series_ prefix, Playlist is its first stage's playlist.</summary>
        public record Series(string Id, string Name, string Playlist);
        /// <summary>A vanilla song; GameId and RecordType are in tracks.csv form (no prefix).</summary>
        public record Song(string BgmId, string InfoId, string Name, string SeriesId, string ToneId, string GameId,
            string Author, string Copyright, string RecordType, string SpecialCategory, string Info1, short TestDispOrder);

        private const string Locale = "us_en";

        private static readonly ConcurrentDictionary<string, Lazy<Catalog>> Cache = new();
        private static readonly PropertyInfo[] PlaylistOrder = PlaylistProperties("Order");
        private static readonly PropertyInfo[] PlaylistIncidence = PlaylistProperties("Incidence");

        private readonly IOptionsMonitor<Sma5hOptions> _config;
        private readonly IServiceProvider _services;

        // Providers are resolved on first load, and only when the game files exist.
        public VanillaCatalogService(IOptionsMonitor<Sma5hOptions> config, IServiceProvider services)
        {
            _config = config;
            _services = services;
        }

        /// <summary>The vanilla catalog, or null when the game files are missing.</summary>
        public virtual Catalog Get()
        {
            var gamePath = Path.GetFullPath(_config.CurrentValue.GameResourcesPath);
            return Cache.GetOrAdd(gamePath, path => new Lazy<Catalog>(() => Load(path))).Value;
        }

        public PlaylistInfoData GetPlaylistInfo()
        {
            var catalog = Get() ?? throw new DesktopApiException("Game resources not found in Resources/Game.");
            return catalog.PlaylistInfo;
        }

        public class Catalog
        {
            public Dictionary<string, string> BgmTitles { get; init; }
            public List<GameTitle> GameTitles { get; init; }
            /// <summary>Series with sound test songs, in sound test order.</summary>
            public List<Series> Series { get; init; }
            /// <summary>Songs in sound test order, then the hidden ones (TestDispOrder &lt; 0).</summary>
            public List<Song> Songs { get; init; }
            public PlaylistInfoData PlaylistInfo { get; init; }
        }

        private Catalog Load(string gamePath)
        {
            string Game(string relative) => Path.Combine(gamePath, relative);
            var files = new[]
            {
                Game(PrcExtConstants.PRC_UI_BGM_DB_PATH), Game(PrcExtConstants.PRC_UI_GAMETITLE_DB_PATH),
                Game(PrcExtConstants.PRC_UI_STAGE_DB_PATH), Game(PrcExtConstants.PRC_UI_SERIES_DB_PATH),
                Game(string.Format(MsbtExtConstants.MSBT_BGM, Locale)), Game(string.Format(MsbtExtConstants.MSBT_TITLE, Locale))
            };
            if (!files.All(File.Exists))
                return null;

            var providers = _services.GetServices<IResourceProvider>().ToList();
            var prc = providers.OfType<PrcResourceProvider>().First();
            var msbt = providers.OfType<MsbtResourceProvider>().First();
            var bgmDb = prc.ReadFile<PrcUiBgmDatabase>(files[0]);
            var gameTitleDb = prc.ReadFile<PrcUiGameTitleDatabase>(files[1]);
            var stageDb = prc.ReadFile<PrcUiStageDatabase>(files[2]);
            var seriesDb = prc.ReadFile<PrcUiSeriesDatabase>(files[3]);
            var bgmMsbt = msbt.ReadFile<MsbtDatabase>(files[4]).Entries;
            var titleMsbt = msbt.ReadFile<MsbtDatabase>(files[5]).Entries;
            string BgmText(string format, string nameId) =>
                AudioStateService.ConvertFromGameTextTag(bgmMsbt.GetValueOrDefault(string.Format(format, nameId)) ?? "");

            var bgmTitles = new Dictionary<string, string>();
            foreach (var entry in bgmDb.DbRootEntries.Values)
            {
                if (!string.IsNullOrEmpty(entry.UiBgmId))
                    bgmTitles[entry.UiBgmId] = CliUtil.FirstNonEmpty(BgmText(MusicConstants.InternalIds.MSBT_BGM_TITLE, entry.NameId), entry.UiBgmId);
            }

            var gameTitles = gameTitleDb.DbRootEntries.Values
                .Where(g => !string.IsNullOrEmpty(g.UiGameTitleId))
                .Select(g =>
                {
                    var id = g.UiGameTitleId.StartsWith(MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX)
                        ? g.UiGameTitleId[MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX.Length..]
                        : g.UiGameTitleId;
                    return new GameTitle(id, CliUtil.FirstNonEmpty(titleMsbt.GetValueOrDefault("tit_" + g.NameId), id), g.UiSeriesId ?? "");
                })
                .ToList();

            var seriesByGameTitle = gameTitleDb.DbRootEntries.Values
                .Where(g => !string.IsNullOrEmpty(g.UiGameTitleId))
                .ToDictionary(g => g.UiGameTitleId, g => g.UiSeriesId ?? "");
            var songs = new List<Song>();
            foreach (var entry in bgmDb.DbRootEntries.Values.OrderBy(e => e.TestDispOrder < 0).ThenBy(e => e.TestDispOrder))
            {
                if (string.IsNullOrEmpty(entry.UiBgmId) || entry.StreamSetId == null || !bgmDb.StreamSetEntries.TryGetValue(entry.StreamSetId, out var set)) continue;
                var infoId = set.Info0;
                if (infoId == null || !infoId.StartsWith("info_")) continue;
                var streamId = bgmDb.AssignedInfoEntries.GetValueOrDefault(infoId)?.StreamId;
                var toneId = streamId != null ? bgmDb.StreamPropertyEntries.GetValueOrDefault(streamId)?.DataName0 : null;
                songs.Add(new Song(entry.UiBgmId, infoId, bgmTitles[entry.UiBgmId],
                    seriesByGameTitle.GetValueOrDefault(entry.UiGameTitleId ?? "", ""), toneId,
                    WithoutPrefix(entry.UiGameTitleId ?? "", MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX),
                    BgmText(MusicConstants.InternalIds.MSBT_BGM_AUTHOR, entry.NameId), BgmText(MusicConstants.InternalIds.MSBT_BGM_COPYRIGHT, entry.NameId),
                    WithoutPrefix(entry.RecordType ?? "", MusicConstants.InternalIds.RECORD_TYPE_PREFIX), set.SpecialCategory ?? "", set.Info1 ?? "",
                    entry.TestDispOrder));
            }

            // A series' playlist is the one its first stage uses.
            var playlistBySeries = stageDb.DbRootEntries.Values
                .Where(s => !string.IsNullOrEmpty(s.UiSeriesId) && !string.IsNullOrEmpty(s.BgmSetId))
                .GroupBy(s => s.UiSeriesId)
                .ToDictionary(g => g.Key, g => g.First().BgmSetId);
            var soundTestSeries = songs.Where(s => s.TestDispOrder >= 0).Select(s => s.SeriesId).ToHashSet();
            var series = seriesDb.DbRootEntries.Values
                .Where(s => soundTestSeries.Contains(s.UiSeriesId))
                .OrderBy(s => s.DispOrderSound)
                .Select(s => new Series(WithoutPrefix(s.UiSeriesId, MusicConstants.InternalIds.SERIES_ID_PREFIX),
                    CliUtil.FirstNonEmpty(titleMsbt.GetValueOrDefault(string.Format(MusicConstants.InternalIds.MSBT_SERIES_TITLE, s.NameId)),
                        DisplayNames.Series.GetValueOrDefault(s.UiSeriesId), s.NameId),
                    playlistBySeries.GetValueOrDefault(s.UiSeriesId, "")))
                .ToList();

            return new Catalog
            {
                BgmTitles = bgmTitles,
                GameTitles = gameTitles,
                Series = series,
                Songs = songs,
                PlaylistInfo = BuildPlaylistInfo(bgmDb, stageDb, bgmTitles)
            };
        }

        private static PlaylistInfoData BuildPlaylistInfo(PrcUiBgmDatabase bgmDb, PrcUiStageDatabase stageDb, Dictionary<string, string> bgmTitles)
        {
            var tracksByPlaylist = bgmDb.PlaylistEntries.ToDictionary(p => p.Id, p => p.Values);
            var stages = stageDb.DbRootEntries.Values.ToList();

            // playlist → series of the stages that use it
            var seriesByPlaylist = stages
                .Where(s => !string.IsNullOrEmpty(s.BgmSetId))
                .GroupBy(s => s.BgmSetId)
                .ToDictionary(g => g.Key, g => g.Select(s => s.UiSeriesId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet());

            var playlists = tracksByPlaylist
                .Select(p => new PlaylistInfo(
                    p.Key,
                    DisplayNames.Playlists.GetValueOrDefault(p.Key, p.Key),
                    seriesByPlaylist.GetValueOrDefault(p.Key, new HashSet<string>())
                        .Select(id => DisplayNames.Series.GetValueOrDefault(id, id))
                        .Where(name => name.Length > 0 && name != "None")
                        .OrderBy(name => name, StringComparer.InvariantCulture)
                        .ToList(),
                    p.Value.Count))
                .OrderBy(p => p.Name, StringComparer.InvariantCulture)
                .ToList();

            var stageInfos = stages
                .Select(stage =>
                {
                    var name = DisplayNames.Stages.GetValueOrDefault(stage.UiStageId, stage.UiStageId);
                    var playlistId = stage.BgmSetId ?? "";
                    var order = stage.BgmSettingNo;
                    var songs = tracksByPlaylist.GetValueOrDefault(playlistId, new List<PrcBgmPlaylistEntry>())
                        .Where(track => order < PlaylistIncidence.Length && Convert.ToInt32(PlaylistIncidence[order].GetValue(track)) > 0)
                        .Select(track => new StageSong(
                            Convert.ToInt32(PlaylistOrder[order].GetValue(track)),
                            track.UiBgmId,
                            bgmTitles.GetValueOrDefault(track.UiBgmId, track.UiBgmId)))
                        .OrderBy(s => s.Order)
                        .ToList();
                    return new StageInfo(stage.UiStageId, name, name.StartsWith("(H)"), stage.UiSeriesId ?? "",
                        DisplayNames.Series.GetValueOrDefault(stage.UiSeriesId ?? "", stage.UiSeriesId ?? ""),
                        playlistId, DisplayNames.Playlists.GetValueOrDefault(playlistId, playlistId), order, songs);
                })
                .OrderBy(s => s.Hidden)
                .ThenBy(s => s.Name, StringComparer.InvariantCulture)
                .ToList();

            return new PlaylistInfoData(playlists, stageInfos);
        }

        private static string WithoutPrefix(string id, string prefix) => id.StartsWith(prefix) ? id[prefix.Length..] : id;

        private static PropertyInfo[] PlaylistProperties(string prefix) =>
            Enumerable.Range(0, 16).Select(i => typeof(PrcBgmPlaylistEntry).GetProperty(prefix + i)).ToArray();
    }
}
