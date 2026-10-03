using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Data;
using Sma5h.Data.Ui.Param.Database;
using Sma5h.Data.Ui.Param.Database.PrcUiBgmDatabaseModels;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Helpers;
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
        public record Song(string BgmId, string InfoId, string Name, string SeriesId, string Author = "",
            string Copyright = "", string Game = "", string RecordType = "original", string Info1 = "",
            string SpecialCategory = "", string AudioPath = "", float BaseVolume = 1, bool InSoundtest = true);

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
            public List<Song> Songs { get; init; }
            public PlaylistInfoData PlaylistInfo { get; init; }
        }

        private Catalog Load(string gamePath)
        {
            string Game(string relative) => Path.Combine(gamePath, relative);
            var files = new[]
            {
                Game(PrcExtConstants.PRC_UI_BGM_DB_PATH), Game(PrcExtConstants.PRC_UI_GAMETITLE_DB_PATH),
                Game(PrcExtConstants.PRC_UI_STAGE_DB_PATH),
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
            var bgmMsbt = msbt.ReadFile<MsbtDatabase>(files[3]).Entries;
            var titleMsbt = msbt.ReadFile<MsbtDatabase>(files[4]).Entries;

            var bgmTitles = new Dictionary<string, string>();
            foreach (var entry in bgmDb.DbRootEntries.Values)
            {
                if (!string.IsNullOrEmpty(entry.UiBgmId))
                    bgmTitles[entry.UiBgmId] = CliUtil.FirstNonEmpty(bgmMsbt.GetValueOrDefault("bgm_title_" + entry.NameId), entry.UiBgmId);
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
            var volumes = TracksCsv.ReadLenient(Path.Combine(_config.CurrentValue.ResourcesPath, MusicConstants.Resources.NUS3BANK_IDS_FILE))
                .ToDictionary(r => r.Get("NUS3Bank Name"), r => VolumeConfigService.ParseVolume(r.Get("Volume")));
            var songs = new List<Song>();
            foreach (var entry in bgmDb.DbRootEntries.Values.OrderBy(e => e.TestDispOrder))
            {
                if (string.IsNullOrEmpty(entry.UiBgmId)) continue;
                var infoId = entry.StreamSetId != null && bgmDb.StreamSetEntries.TryGetValue(entry.StreamSetId, out var set) ? set.Info0 : null;
                if (infoId == null || !infoId.StartsWith("info_")) continue;
                var streamSet = bgmDb.StreamSetEntries[entry.StreamSetId];
                var streamId = bgmDb.AssignedInfoEntries.TryGetValue(infoId, out var info) ? info.StreamId : null;
                var toneId = streamId != null && bgmDb.StreamPropertyEntries.TryGetValue(streamId, out var stream) ? stream.DataName0 : null;
                var audioPath = toneId == null ? "" : Game(Path.Combine("stream;", "sound", "bgm", $"bgm_{toneId}.nus3audio"));
                var baseVolume = volumes.GetValueOrDefault("bgm_" + toneId, 1);
                songs.Add(new Song(entry.UiBgmId, infoId, bgmTitles[entry.UiBgmId],
                    seriesByGameTitle.GetValueOrDefault(entry.UiGameTitleId ?? "", ""),
                    bgmMsbt.GetValueOrDefault("bgm_author_" + entry.NameId, ""),
                    bgmMsbt.GetValueOrDefault("bgm_copyright_" + entry.NameId, ""),
                    (entry.UiGameTitleId ?? "").Replace(MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX, ""),
                    (entry.RecordType ?? "original").Replace("record_", ""), streamSet.Info1 ?? "",
                    streamSet.SpecialCategory ?? "", audioPath, baseVolume, entry.TestDispOrder >= 0));
            }

            return new Catalog
            {
                BgmTitles = bgmTitles,
                GameTitles = gameTitles,
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

        private static PropertyInfo[] PlaylistProperties(string prefix) =>
            Enumerable.Range(0, 16).Select(i => typeof(PrcBgmPlaylistEntry).GetProperty(prefix + i)).ToArray();
    }
}
