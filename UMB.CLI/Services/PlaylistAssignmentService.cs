using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Edits a mod's playlists and stage playlists. Load shows them as the build will, with each changed game or
    /// series playlist's default; Save writes playlists.toml and moves legacy series.toml [[playlists]] blocks into it.
    /// </summary>
    public class PlaylistAssignmentService
    {
        private const string PlaylistsTable = "playlists";
        /// <summary>A legacy series.toml playlist: WholeSeries for series-playlist and blocks without a song list.</summary>
        private record LegacyPlaylist(string PlaylistId, string SeriesId, bool WholeSeries);

        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly VanillaCatalogService _catalog;

        public PlaylistAssignmentService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, VanillaCatalogService catalog)
        {
            _musicConfig = musicConfig;
            _catalog = catalog;
        }

        public ManagePlaylistsData Load(string modPath)
        {
            var catalog = _catalog.Get() ?? throw new DesktopApiException("Game resources not found in Resources/Game.");
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var mod = new FolderMusicMod(NullLogger<IMusicMod>.Instance, null, resolvedModPath).GetMusicModEntries();

            var seriesByGame = catalog.GameTitles.ToDictionary(g => MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX + g.Id, g => g.SeriesId);
            foreach (var game in mod.GameTitleEntries)
                seriesByGame.TryAdd(game.UiGameTitleId, game.UiSeriesId);
            var modSongs = PlaylistHelper.SoundTestSongs(mod.BgmDbRootEntries, seriesByGame);
            var stages = catalog.Stages.Select(Copy).ToList();
            var required = PlaylistHelper.RequiredSongs(stages, mod.SeriesPlaylists, modSongs, PlaylistHelper.SmashBattleIncidence(_musicConfig.CurrentValue.Sma5hMusic));
            var autoAdded = required.Select(r => (r.PlaylistId, r.UiBgmId)).ToHashSet();

            // Defaults: each game and series playlist without its saved tracks or legacy blocks.
            var defaults = catalog.Playlists.ToDictionary(p => p.Id, PlaylistHelper.Clone);
            foreach (var playlist in mod.SeriesPlaylists)
                PlaylistHelper.Merge(defaults, PlaylistHelper.Clone(playlist));
            PlaylistHelper.ApplyModPlaylists(defaults, stages, required, Array.Empty<PlaylistEntry>(), Array.Empty<StagePlaylistAssignment>());

            // As the build: game playlists, the mod's series playlists, then playlists.toml and the songs the build keeps.
            var playlists = catalog.Playlists.ToDictionary(p => p.Id, PlaylistHelper.Clone);
            foreach (var playlist in mod.PlaylistEntries)
                PlaylistHelper.Merge(playlists, PlaylistHelper.Clone(playlist));
            PlaylistHelper.ApplyModPlaylists(playlists, stages, required, mod.PlaylistOverrides, mod.StageAssignments);

            var seriesNames = catalog.Series.ToDictionary(s => MusicConstants.InternalIds.SERIES_ID_PREFIX + s.Id, s => s.Name);
            foreach (var (id, name) in DisplayNames.Series)
                seriesNames.TryAdd(id, name);
            foreach (var series in mod.SeriesEntries)
                seriesNames[series.UiSeriesId] = series.MSBTTitle.GetValueOrDefault("en_us", series.NameId);
            string SeriesName(string uiSeriesId) => seriesNames.GetValueOrDefault(uiSeriesId ?? "", uiSeriesId ?? "");

            var vanillaTitles = mod.VanillaBgmOverrides.Where(o => !string.IsNullOrEmpty(o.Title)).ToDictionary(o => o.UiBgmId, o => o.Title);
            var songs = catalog.Songs
                .Select(s => new PlaylistSong(s.BgmId, vanillaTitles.GetValueOrDefault(s.BgmId, s.Name), SeriesName(s.SeriesId), false))
                .Concat(mod.BgmDbRootEntries.Select(b => new PlaylistSong(b.UiBgmId, b.Title.GetValueOrDefault("en_us", b.UiBgmId),
                    SeriesName(seriesByGame.GetValueOrDefault(b.UiGameTitleId ?? "")), true)))
                .GroupBy(s => s.BgmId).Select(g => g.Last())
                .ToList();

            // Legacy series.toml playlists: series-playlist and wildcard blocks become series flags when migrated.
            var legacy = LegacyPlaylists(resolvedModPath);
            var seriesFlags = PlaylistsToml.Read(resolvedModPath).Playlists.SelectMany(p => p.Series.Select(series => (PlaylistId: p.Id, SeriesId: series)))
                .Concat(legacy.Where(l => l.WholeSeries).Select(l => (l.PlaylistId, l.SeriesId)))
                .Distinct()
                .ToLookup(f => f.PlaylistId, f => new SeriesRef(f.SeriesId, SeriesName(MusicConstants.InternalIds.SERIES_ID_PREFIX + f.SeriesId)));

            var vanillaIds = catalog.Playlists.Select(p => p.Id).ToHashSet();
            var managed = playlists.Values
                .Where(p => !PlaylistHelper.IsGameTitlePlaylist(p.Id))
                .Select(p =>
                {
                    var series = seriesFlags[p.Id].ToList();
                    var kind = vanillaIds.Contains(p.Id) ? "vanilla" : series.Count > 0 ? "series" : "custom";
                    var name = CliUtil.FirstNonEmpty(DisplayNames.Playlists.GetValueOrDefault(p.Id), p.Title, p.Id);
                    var defaultTracks = kind != "custom" && defaults.TryGetValue(p.Id, out var d) && TrackKey(d) != TrackKey(p) ? Tracks(d, autoAdded) : null;
                    return new ManagedPlaylist(p.Id, name, kind, series, Tracks(p, autoAdded), defaultTracks);
                })
                .OrderBy(p => p.Kind == "custom" ? 0 : p.Kind == "series" ? 1 : 2)
                .ThenBy(p => p.Name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var vanillaStages = catalog.Stages.ToDictionary(s => s.UiStageId);
            var managedStages = stages
                .Select(s =>
                {
                    var name = DisplayNames.Stages.GetValueOrDefault(s.UiStageId, s.UiStageId);
                    var vanilla = vanillaStages[s.UiStageId];
                    return new ManagedStage(s.UiStageId, name, name.StartsWith("(H)"), SeriesName(s.UiSeriesId),
                        vanilla.BgmSetId ?? "", vanilla.BgmSettingNo, s.BgmSetId ?? "", s.BgmSettingNo);
                })
                .Where(s => s.VanillaPlaylistId.Length > 0)
                .OrderBy(s => s.Hidden)
                .ThenBy(s => s.Name, StringComparer.InvariantCulture)
                .ToList();

            return new ManagePlaylistsData(Path.GetFileName(resolvedModPath), resolvedModPath, managed, managedStages, songs, legacy.Count > 0);
        }

        /// <summary>
        /// Writes the given playlists and stage playlists to playlists.toml and drops the legacy series.toml playlists
        /// (series-playlist and [[playlists]] blocks), which the saved playlists now hold.
        /// </summary>
        public ManagePlaylistsData Save(string modPath, List<ManagedPlaylist> playlists, List<StageAssignmentInput> stages)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            PlaylistsToml.Write(resolvedModPath, new FolderPlaylistsFileConfig
            {
                Playlists = (playlists ?? new()).Select(ToConfig).ToList(),
                Stages = (stages ?? new()).Select(s => new FolderStageConfig { Id = s.Id, Playlist = s.PlaylistId, Order = s.Order }).ToList()
            });
            foreach (var path in SeriesTomlPaths(resolvedModPath))
            {
                var lines = SeriesToml.RewriteArrayTables(SeriesToml.ReadLines(path), PlaylistsTable, new List<List<string>>());
                SeriesToml.WriteLines(path, SeriesToml.UpsertTable(lines, "series", new[] { new SeriesToml.Entry(PlaylistsToml.LegacySeriesPlaylistKey, null) }, createIfMissing: false));
            }
            return Load(resolvedModPath);
        }

        /// <summary>Game playlists keep their game name, so only other playlists save one.</summary>
        private static FolderPlaylistConfig ToConfig(ManagedPlaylist playlist) => new()
        {
            Id = playlist.Id,
            Name = playlist.Kind == "vanilla" ? null : playlist.Name,
            Series = playlist.Series?.Select(s => s.Id).ToList() ?? new(),
            Tracks = playlist.Tracks?.Select(t => new FolderPlaylistTrackConfig { Song = t.BgmId, Order = t.Orders, Incidence = t.Incidences }).ToList()
        };

        private static List<LegacyPlaylist> LegacyPlaylists(string modPath) =>
            SeriesTomlPaths(modPath)
                .SelectMany(path =>
                {
                    var text = File.ReadAllText(path);
                    var seriesId = SeriesToml.ReadHeader(text).Id;
                    return SeriesToml.TableArrayBlocks(text, PlaylistsTable)
                        .Select(block => new LegacyPlaylist(SeriesToml.NonEmptyString(block, "id"), seriesId,
                            !Regex.IsMatch(block, @"^\s*songs\s*=\s*\[", RegexOptions.Multiline)))
                        .Append(new LegacyPlaylist(SeriesToml.NonEmptyString(SeriesToml.TableSection(text, "series") ?? text, PlaylistsToml.LegacySeriesPlaylistKey), seriesId, true));
                })
                .Where(l => l.PlaylistId != null && l.SeriesId != null)
                .ToList();

        private static List<PlaylistTrack> Tracks(PlaylistEntry playlist, HashSet<(string, string)> autoAdded) =>
            playlist.Tracks.Select(t => new PlaylistTrack(t.UiBgmId, PlaylistHelper.Orders(t), PlaylistHelper.Incidences(t), autoAdded.Contains((playlist.Id, t.UiBgmId)))).ToList();

        private static string TrackKey(PlaylistEntry playlist) =>
            string.Join(";", playlist.Tracks.Select(t => $"{t.UiBgmId}:{string.Join(",", PlaylistHelper.Orders(t))}:{string.Join(",", PlaylistHelper.Incidences(t))}"));

        private static IEnumerable<string> SeriesTomlPaths(string modPath) =>
            ModPaths.VisibleDirs(modPath)
                .Select(dir => Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE))
                .Where(File.Exists);

        private static StageEntry Copy(StageEntry stage) => new()
        {
            UiStageId = stage.UiStageId,
            UiSeriesId = stage.UiSeriesId,
            BgmSetId = stage.BgmSetId,
            BgmSettingNo = stage.BgmSettingNo
        };
    }
}
