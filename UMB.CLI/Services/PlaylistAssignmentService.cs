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
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Edits a mod's playlists and stage playlists. Load shows them as the build will (game playlists,
    /// then the mod's series playlists and songs, then playlists.toml); Save writes playlists.toml and
    /// moves any series.toml [[playlists]] blocks into it.
    /// </summary>
    public class PlaylistAssignmentService
    {
        private const string PlaylistsTable = "playlists";

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
            var mod = new FolderMusicMod(NullLogger<IMusicMod>.Instance, null, resolvedModPath, readAudioMetadata: false).GetMusicModEntries();

            // The build's order: game playlists, mod series playlists, playlists.toml playlists, mod songs on their stages, stages.
            var playlists = catalog.Playlists.ToDictionary(p => p.Id, Copy);
            var stages = catalog.Stages.Select(Copy).ToList();
            foreach (var playlist in mod.PlaylistEntries)
                PlaylistHelper.Merge(playlists, playlist);
            var seriesByGame = catalog.GameTitles.ToDictionary(g => MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX + g.Id, g => g.SeriesId);
            foreach (var game in mod.GameTitleEntries)
                seriesByGame.TryAdd(game.UiGameTitleId, game.UiSeriesId);
            var modSongs = mod.BgmDbRootEntries
                .Where(b => b.TestDispOrder >= 0)
                .Select(b => (b.UiBgmId, UiSeriesId: seriesByGame.GetValueOrDefault(b.UiGameTitleId ?? "")))
                .ToList();
            var modSongPlaylists = PlaylistHelper.ModSongPlaylists(stages);
            var autoAdded = modSongs.SelectMany(s => modSongPlaylists(s.UiSeriesId).Select(id => (id, s.UiBgmId))).ToHashSet();
            PlaylistHelper.ApplyPlaylists(playlists, mod.PlaylistOverrides);
            PlaylistHelper.AddModSongs(playlists, stages, modSongs);
            PlaylistHelper.ApplyStages(stages, mod.StageAssignments);

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

            var vanillaIds = catalog.Playlists.Select(p => p.Id).ToHashSet();
            var seriesPlaylistIds = mod.PlaylistEntries.Select(p => p.Id).ToHashSet();
            var blockIds = SeriesTomlPaths(resolvedModPath)
                .SelectMany(path => SeriesToml.TableArrayBlocks(File.ReadAllText(path), PlaylistsTable))
                .Select(block => SeriesToml.NonEmptyString(block, "id"))
                .Where(id => id != null)
                .ToHashSet();
            var persistedIds = mod.PlaylistOverrides.Select(p => p.Id).Concat(blockIds).ToHashSet();
            seriesPlaylistIds.ExceptWith(blockIds);

            var managed = playlists.Values
                .Select(p =>
                {
                    var kind = vanillaIds.Contains(p.Id) ? "vanilla" : seriesPlaylistIds.Contains(p.Id) ? "series" : "custom";
                    var name = CliUtil.FirstNonEmpty(p.Title, DisplayNames.Playlists.GetValueOrDefault(p.Id),
                        p.Id.StartsWith(PlaylistHelper.GameTitlePlaylistPrefix) ? SeriesName(MusicConstants.InternalIds.SERIES_ID_PREFIX + p.Id[PlaylistHelper.GameTitlePlaylistPrefix.Length..]) : null,
                        p.Id);
                    var tracks = p.Tracks.Select(t => new PlaylistTrack(t.UiBgmId, PlaylistHelper.Orders(t), PlaylistHelper.Incidences(t), autoAdded.Contains((p.Id, t.UiBgmId)))).ToList();
                    return new ManagedPlaylist(p.Id, name, kind, persistedIds.Contains(p.Id), tracks);
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

            return new ManagePlaylistsData(Path.GetFileName(resolvedModPath), resolvedModPath, managed, managedStages, songs);
        }

        /// <summary>
        /// Writes the given playlists and stage playlists to playlists.toml (removing it when both are empty)
        /// and drops every series' [[playlists]] blocks, which the saved playlists now hold.
        /// </summary>
        public ManagePlaylistsData Save(string modPath, List<ManagedPlaylist> playlists, List<StageAssignmentInput> stages)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var tomlPath = Path.Combine(resolvedModPath, MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE);
            playlists ??= new List<ManagedPlaylist>();
            stages ??= new List<StageAssignmentInput>();

            if (playlists.Count + stages.Count == 0)
                File.Delete(tomlPath);
            else
                SeriesToml.WriteLines(tomlPath, PlaylistsTomlLines(playlists, stages));

            foreach (var path in SeriesTomlPaths(resolvedModPath))
                SeriesToml.WriteLines(path, SeriesToml.RewriteArrayTables(SeriesToml.ReadLines(path), PlaylistsTable, new List<List<string>>()));

            return Load(resolvedModPath);
        }

        private static List<string> PlaylistsTomlLines(List<ManagedPlaylist> playlists, List<StageAssignmentInput> stages)
        {
            var lines = new List<string>();
            foreach (var playlist in playlists)
            {
                lines.Add("[[playlists]]");
                lines.Add($"id = \"{SeriesToml.Escape(playlist.Id)}\"");
                if (!string.IsNullOrEmpty(playlist.Name) && playlist.Kind == "custom")
                    lines.Add($"name = \"{SeriesToml.Escape(playlist.Name)}\"");
                if ((playlist.Tracks ?? new()).Count == 0)
                    lines.Add("tracks = []");
                else
                {
                    lines.Add("tracks = [");
                    lines.AddRange(playlist.Tracks.Select(t =>
                        $"    {{ song = \"{SeriesToml.Escape(t.BgmId)}\", order = {Slots(t.Orders)}, incidence = {Slots(t.Incidences)} }},"));
                    lines.Add("]");
                }
                lines.Add("");
            }
            foreach (var stage in stages)
            {
                lines.Add("[[stages]]");
                lines.Add($"id = \"{SeriesToml.Escape(stage.Id)}\"");
                lines.Add($"playlist = \"{SeriesToml.Escape(stage.PlaylistId)}\"");
                lines.Add($"order = {Math.Clamp(stage.Order, 0, PlaylistHelper.OrderCount - 1)}");
                lines.Add("");
            }
            return lines;
        }

        /// <summary>One value when every order slot shares it, else the 16 values.</summary>
        private static string Slots<T>(IReadOnlyList<T> values) =>
            values.Distinct().Count() == 1 ? $"{values[0]}" : $"[{string.Join(", ", values)}]";

        private static IEnumerable<string> SeriesTomlPaths(string modPath) =>
            ModPaths.VisibleDirs(modPath)
                .Select(dir => Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE))
                .Where(File.Exists);

        private static PlaylistEntry Copy(PlaylistEntry playlist)
        {
            var copy = new PlaylistEntry(playlist.Id, playlist.Title);
            copy.Tracks.AddRange(playlist.Tracks.Select(t => PlaylistHelper.Track(t.UiBgmId, PlaylistHelper.Orders(t), PlaylistHelper.Incidences(t))));
            return copy;
        }

        private static StageEntry Copy(StageEntry stage) => new()
        {
            UiStageId = stage.UiStageId,
            UiSeriesId = stage.UiSeriesId,
            BgmSetId = stage.BgmSetId,
            BgmSettingNo = stage.BgmSettingNo
        };
    }
}
