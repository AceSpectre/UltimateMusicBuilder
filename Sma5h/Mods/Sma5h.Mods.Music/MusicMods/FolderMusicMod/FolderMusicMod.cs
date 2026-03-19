using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;

namespace Sma5h.Mods.Music.MusicMods.FolderMusicMod
{
    /// <summary>
    /// Loads a folder-based music mod. Each subfolder of the mod path represents
    /// one series and must contain a series.toml and tracks.csv alongside audio files.
    /// This format is read-only — write operations return false.
    /// </summary>
    public class FolderMusicMod : IMusicMod
    {
        private readonly ILogger _logger;
        private readonly IAudioMetadataService _audioMetadataService;
        private readonly string _modPath;
        private readonly FolderMusicModInformation _modInfo;

        public string Id => _modInfo.Id;
        public string Name => _modInfo.Name;
        public string ModPath => _modPath;
        public MusicModInformation Mod => _modInfo;

        public FolderMusicMod(ILogger<IMusicMod> logger, IAudioMetadataService audioMetadataService, string modPath)
        {
            _logger = logger;
            _audioMetadataService = audioMetadataService;
            _modPath = modPath;
            var folderName = Path.GetFileName(modPath);
            _modInfo = new FolderMusicModInformation(folderName, folderName);
        }

        public MusicModEntries GetMusicModEntries()
        {
            var output = new MusicModEntries();
            var seenToneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var subfolder in Directory.GetDirectories(_modPath))
            {
                var tomlPath = Path.Combine(subfolder, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                var csvPath = Path.Combine(subfolder, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);

                if (!File.Exists(tomlPath))
                {
                    _logger.LogDebug("Skipping subfolder {Subfolder}: no series.toml found.", subfolder);
                    continue;
                }
                if (!File.Exists(csvPath))
                {
                    _logger.LogWarning("Subfolder {Subfolder} has series.toml but no tracks.csv — skipping.", subfolder);
                    continue;
                }

                FolderSeriesFileConfig seriesFile;
                try
                {
                    seriesFile = ParseSeriesFile(tomlPath);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to parse series.toml in {Subfolder}.", subfolder);
                    continue;
                }

                if (seriesFile.Series == null)
                {
                    _logger.LogError("series.toml in {Subfolder} is missing a [series] section.", subfolder);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(seriesFile.Series.Id))
                {
                    _logger.LogError("series.toml in {Subfolder}: [series] id is required.", subfolder);
                    continue;
                }
                if (seriesFile.Games == null || seriesFile.Games.Count == 0)
                {
                    _logger.LogError("series.toml in {Subfolder} has no [[games]] entries.", subfolder);
                    continue;
                }

                var uiSeriesId = MusicConstants.InternalIds.SERIES_ID_PREFIX + seriesFile.Series.Id;
                var isExistingSeries = seriesFile.Series.ExistingSeries;

                // ── SeriesEntry (skip for existing in-game series) ────────
                if (!isExistingSeries)
                {
                    var seriesEntry = new SeriesEntry(uiSeriesId, EntrySource.Mod)
                    {
                        NameId = seriesFile.Series.Id
                    };
                    seriesEntry.MSBTTitle["en_us"] = seriesFile.Series.Name ?? seriesFile.Series.Id;

                    // ── Icon (optional) ────────────────────────────────────
                    var iconPath = Path.Combine(subfolder, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);
                    if (File.Exists(iconPath))
                    {
                        seriesEntry.IconPath = iconPath;
                        _logger.LogInformation("Found icon.png for series {SeriesId}.", uiSeriesId);
                    }
                    output.SeriesEntries.Add(seriesEntry);
                }
                else
                {
                    _logger.LogInformation("Series {SeriesId} is flagged as existing — skipping SeriesEntry creation.", uiSeriesId);
                }

                // ── GameTitleEntries (skip for existing in-game series) ───
                var gameIdLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var game in seriesFile.Games)
                {
                    if (string.IsNullOrWhiteSpace(game.Id))
                    {
                        _logger.LogWarning("Skipping game with empty id in {Subfolder}.", subfolder);
                        continue;
                    }
                    var uiGameTitleId = MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX + game.Id;
                    if (!isExistingSeries)
                    {
                        var gameTitleEntry = new GameTitleEntry(uiGameTitleId, EntrySource.Mod)
                        {
                            NameId = game.Id,
                            UiSeriesId = uiSeriesId
                        };
                        gameTitleEntry.MSBTTitle["en_us"] = game.Name ?? game.Id;
                        output.GameTitleEntries.Add(gameTitleEntry);
                    }
                    gameIdLookup[game.Id] = uiGameTitleId;
                }

                // ── Tracks ─────────────────────────────────────────────────
                IEnumerable<FolderTrackCsvRow> tracks;
                try
                {
                    tracks = ParseTracksFile(csvPath).ToList();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to parse tracks.csv in {Subfolder}.", subfolder);
                    continue;
                }

                var playlistTracks = new List<(string uiBgmId, int incidence)>();
                var filenameToInfoId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var deferredInfo1 = new List<(BgmStreamSetEntry streamSet, string info1Filename)>();

                foreach (var row in tracks)
                {
                    if (string.IsNullOrWhiteSpace(row.Filename))
                    {
                        _logger.LogWarning("Skipping row with empty filename in {CsvPath}.", csvPath);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.Game))
                    {
                        _logger.LogWarning("Skipping {Filename}: game column is required.", row.Filename);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.Title))
                    {
                        _logger.LogWarning("Skipping {Filename}: title column is required.", row.Filename);
                        continue;
                    }
                    if (!gameIdLookup.TryGetValue(row.Game, out var uiGameTitleId))
                    {
                        _logger.LogError("Skipping {Filename}: game '{Game}' not found in series.toml [[games]].", row.Filename, row.Game);
                        continue;
                    }

                    var audioFile = Path.Combine(subfolder, row.Filename);
                    if (!File.Exists(audioFile))
                    {
                        _logger.LogError("Skipping {Filename}: audio file not found at {AudioPath}.", row.Filename, audioFile);
                        continue;
                    }

                    var toneId = DeriveToneId(row.Filename);
                    if (!seenToneIds.Add(toneId))
                    {
                        _logger.LogError("Skipping {Filename}: derived tone_id '{ToneId}' is already used by another track.", row.Filename, toneId);
                        continue;
                    }

                    var uiBgmId   = MusicConstants.InternalIds.UI_BGM_ID_PREFIX + toneId;
                    var setId     = MusicConstants.InternalIds.STREAM_SET_PREFIX + toneId;
                    var infoId    = MusicConstants.InternalIds.INFO_ID_PREFIX + toneId;
                    var streamId  = MusicConstants.InternalIds.STREAM_PREFIX + toneId;

                    // BgmDbRootEntry
                    var dbRoot = new BgmDbRootEntry(uiBgmId, this)
                    {
                        NameId       = toneId,
                        UiGameTitleId = uiGameTitleId,
                        StreamSetId  = setId,
                        RecordType   = NormalizeRecordType(row.RecordType)
                    };
                    dbRoot.Title["en_us"] = row.Title;
                    if (!string.IsNullOrEmpty(row.Author))    dbRoot.Author["en_us"]    = row.Author;
                    if (!string.IsNullOrEmpty(row.Copyright)) dbRoot.Copyright["en_us"] = row.Copyright;
                    output.BgmDbRootEntries.Add(dbRoot);

                    // BgmStreamSetEntry
                    var streamSet = new BgmStreamSetEntry(setId, this)
                    {
                        Info0 = infoId
                    };
                    if (!string.IsNullOrEmpty(row.SpecialCategory))
                        streamSet.SpecialCategory = row.SpecialCategory;
                    output.BgmStreamSetEntries.Add(streamSet);

                    filenameToInfoId[row.Filename] = infoId;
                    if (!string.IsNullOrEmpty(row.Info1))
                        deferredInfo1.Add((streamSet, row.Info1));

                    // BgmAssignedInfoEntry (defaults set by constructor)
                    var assignedInfo = new BgmAssignedInfoEntry(infoId, this)
                    {
                        StreamId = streamId
                    };
                    output.BgmAssignedInfoEntries.Add(assignedInfo);

                    // BgmStreamPropertyEntry (defaults set by constructor)
                    var streamProp = new BgmStreamPropertyEntry(streamId, this)
                    {
                        DataName0 = toneId
                    };
                    output.BgmStreamPropertyEntries.Add(streamProp);

                    // BgmPropertyEntry
                    var bgmProp = new BgmPropertyEntry(toneId, audioFile, this)
                    {
                        AudioVolume = row.Volume
                    };
                    try
                    {
                        AudioCuePoints cuePoints = null;

                        // For .nus3audio files, parse the inner OPUS header directly.
                        // VGMStream cannot open nus3audio containers, producing NaN-based garbage values.
                        if (Path.GetExtension(audioFile).Equals(".nus3audio", StringComparison.OrdinalIgnoreCase))
                            cuePoints = ReadNus3AudioOpusCuePoints(audioFile);

                        // For other formats, use the audio metadata service (VGMStream)
                        if (cuePoints == null)
                            cuePoints = _audioMetadataService.GetCuePoints(audioFile).GetAwaiter().GetResult();

                        bgmProp.TotalTimeMs = cuePoints.TotalTimeMs;
                        bgmProp.TotalSamples = cuePoints.TotalSamples;
                        bgmProp.LoopStartMs = cuePoints.LoopStartMs;
                        bgmProp.LoopStartSample = cuePoints.LoopStartSample;
                        bgmProp.LoopEndMs = cuePoints.LoopEndMs;
                        bgmProp.LoopEndSample = cuePoints.LoopEndSample;
                        _logger.LogInformation("Audio metadata for {ToneId}: {TotalTimeMs}ms, {TotalSamples} samples.", toneId, cuePoints.TotalTimeMs, cuePoints.TotalSamples);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not read audio metadata for {Filename}. Song length will show as 0.", row.Filename);
                    }
                    output.BgmPropertyEntries.Add(bgmProp);

                    playlistTracks.Add((uiBgmId, seriesFile.Series.PlaylistIncidence));
                }

                // ── Resolve info1 references ─────────────────────────────
                foreach (var (streamSet, info1Filename) in deferredInfo1)
                {
                    if (info1Filename.StartsWith("info_"))
                    {
                        // Direct base game info ID reference
                        streamSet.Info1 = info1Filename;
                    }
                    else if (filenameToInfoId.TryGetValue(info1Filename, out var info1Id))
                    {
                        streamSet.Info1 = info1Id;
                    }
                    else
                    {
                        _logger.LogWarning("Track {StreamSetId}: info1 references '{Info1Filename}' which was not found in this series.", streamSet.StreamSetId, info1Filename);
                    }
                }

                // ── Playlist entries ───────────────────────────────────────
                // Auto-add all series songs to bgm_gametitle_{series_id}
                if (playlistTracks.Count > 0)
                {
                    var gametitlePlaylistId = "bgm_gametitle_" + seriesFile.Series.Id;
                    output.PlaylistEntries.Add(BuildPlaylistEntry(gametitlePlaylistId, playlistTracks));
                    _logger.LogInformation("Created playlist {PlaylistId} with {TrackCount} track(s).", gametitlePlaylistId, playlistTracks.Count);

                    // Explicit stage playlists from [[playlists]] in series.toml
                    foreach (var playlistOverride in seriesFile.Playlists)
                    {
                        if (string.IsNullOrWhiteSpace(playlistOverride.Id)) continue;
                        var overrideTracks = playlistTracks
                            .Select(t => (t.uiBgmId, playlistOverride.Incidence))
                            .ToList();
                        output.PlaylistEntries.Add(BuildPlaylistEntry(playlistOverride.Id, overrideTracks));
                        _logger.LogInformation("Created playlist {PlaylistId} with {TrackCount} track(s) (from [[playlists]] override).", playlistOverride.Id, overrideTracks.Count);
                    }
                }
            }

            return output;
        }

        // ── Write operations (not supported for folder-based mods) ────────────

        public Task<bool> AddOrUpdateMusicModEntries(MusicModEntries entries)
        {
            _logger.LogWarning("FolderMusicMod '{ModId}' is read-only. Edit series.toml and tracks.csv directly.", Id);
            return Task.FromResult(false);
        }

        public bool ReorderSongs(List<string> list)
        {
            _logger.LogWarning("FolderMusicMod '{ModId}' is read-only. Edit series.toml and tracks.csv directly.", Id);
            return false;
        }

        public bool RemoveMusicModEntries(MusicModDeleteEntries entries)
        {
            _logger.LogWarning("FolderMusicMod '{ModId}' is read-only. Edit series.toml and tracks.csv directly.", Id);
            return false;
        }

        public bool UpdateModInformation(MusicModInformation info)
        {
            _logger.LogWarning("FolderMusicMod '{ModId}' is read-only. Edit series.toml and tracks.csv directly.", Id);
            return false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private FolderSeriesFileConfig ParseSeriesFile(string tomlPath)
        {
            var tomlText = File.ReadAllText(tomlPath);
            var options = new TomlModelOptions
            {
                ConvertPropertyName = ToKebabCase
            };
            return Toml.ToModel<FolderSeriesFileConfig>(tomlText, options: options);
        }

        private IEnumerable<FolderTrackCsvRow> ParseTracksFile(string csvPath)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null
            };
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, config);
            csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
            return csv.GetRecords<FolderTrackCsvRow>().ToList();
        }

        private static string DeriveToneId(string filename)
        {
            var nameOnly = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            var sb = new StringBuilder(nameOnly.Length);
            foreach (var c in nameOnly)
                sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            var toneId = sb.ToString().Trim('_');
            if (toneId.Length > MusicConstants.GameResources.ToneIdMaximumSize)
                toneId = toneId[..MusicConstants.GameResources.ToneIdMaximumSize];
            return toneId;
        }

        private static string NormalizeRecordType(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "record_original";
            // Already prefixed
            if (input.StartsWith("record_"))
                return MusicConstants.VALID_RECORD_TYPES.Contains(input) ? input : "record_original";
            var prefixed = "record_" + input.ToLowerInvariant();
            return MusicConstants.VALID_RECORD_TYPES.Contains(prefixed) ? prefixed : "record_original";
        }

        private static PlaylistEntry BuildPlaylistEntry(string playlistId, List<(string uiBgmId, int incidence)> tracks)
        {
            var entry = new PlaylistEntry(playlistId);
            for (var i = 0; i < tracks.Count; i++)
            {
                var (uiBgmId, incidence) = tracks[i];
                var incidenceVal = (ushort)Math.Clamp(incidence, 0, ushort.MaxValue);
                entry.Tracks.Add(new PlaylistValueEntry
                {
                    UiBgmId     = uiBgmId,
                    Order0      = (short)i,
                    Incidence0  = incidenceVal,
                    Order1      = (short)i, Incidence1  = incidenceVal,
                    Order2      = (short)i, Incidence2  = incidenceVal,
                    Order3      = (short)i, Incidence3  = incidenceVal,
                    Order4      = (short)i, Incidence4  = incidenceVal,
                    Order5      = (short)i, Incidence5  = incidenceVal,
                    Order6      = (short)i, Incidence6  = incidenceVal,
                    Order7      = (short)i, Incidence7  = incidenceVal,
                    Order8      = (short)i, Incidence8  = incidenceVal,
                    Order9      = (short)i, Incidence9  = incidenceVal,
                    Order10     = (short)i, Incidence10 = incidenceVal,
                    Order11     = (short)i, Incidence11 = incidenceVal,
                    Order12     = (short)i, Incidence12 = incidenceVal,
                    Order13     = (short)i, Incidence13 = incidenceVal,
                    Order14     = (short)i, Incidence14 = incidenceVal,
                    Order15     = (short)i, Incidence15 = incidenceVal
                });
            }
            return entry;
        }

        private static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads cue points directly from the Namco OPUS header inside a .nus3audio container.
        /// Returns null if the file doesn't contain an OPUS inner stream.
        /// </summary>
        private AudioCuePoints ReadNus3AudioOpusCuePoints(string filePath)
        {
            using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // Scan for the "PACK" chunk (typically at a fixed offset, but search to be safe)
            var header = reader.ReadBytes((int)Math.Min(fs.Length, 512));
            int packOffset = -1;
            for (int i = 0; i <= header.Length - 4; i++)
            {
                if (header[i] == (byte)'P' && header[i + 1] == (byte)'A' &&
                    header[i + 2] == (byte)'C' && header[i + 3] == (byte)'K')
                {
                    packOffset = i;
                    break;
                }
            }
            if (packOffset < 0)
            {
                _logger.LogWarning("No PACK chunk found in nus3audio file {FilePath}.", filePath);
                return null;
            }

            // Skip PACK magic (4 bytes) + size (4 bytes LE) to reach inner audio data
            int dataStart = packOffset + 8;
            if (dataStart + 4 > header.Length)
                return null;

            var magic = Encoding.ASCII.GetString(header, dataStart, 4);
            if (magic != "OPUS")
            {
                _logger.LogWarning("Inner audio format '{Magic}' in {FilePath} is not OPUS; cannot read cue points.", magic, filePath);
                return null;
            }

            // Namco OPUS header (big-endian) layout from PACK data start:
            // +0x00: "OPUS" (4 bytes)
            // +0x04: padding (4 bytes)
            // +0x08: total samples (4 bytes BE)
            // +0x0C: channel count (4 bytes BE)
            // +0x10: sample rate (4 bytes BE)
            // +0x14: loop start sample (4 bytes BE)
            // +0x18: loop end sample (4 bytes BE)
            int hdrBase = dataStart + 8; // skip "OPUS" + padding
            if (hdrBase + 20 > header.Length)
                return null;

            uint totalSamples   = ReadBE32(header, hdrBase);
            // skip channels at hdrBase + 4
            uint sampleRate     = ReadBE32(header, hdrBase + 8);
            uint loopStartSample = ReadBE32(header, hdrBase + 12);
            uint loopEndSample  = ReadBE32(header, hdrBase + 16);

            if (sampleRate == 0)
            {
                _logger.LogWarning("Sample rate is 0 in OPUS header of {FilePath}.", filePath);
                return null;
            }

            return new AudioCuePoints
            {
                TotalSamples    = totalSamples,
                TotalTimeMs     = (uint)((ulong)totalSamples * 1000 / sampleRate),
                LoopStartSample = loopStartSample,
                LoopStartMs     = (uint)((ulong)loopStartSample * 1000 / sampleRate),
                LoopEndSample   = loopEndSample,
                LoopEndMs       = (uint)((ulong)loopEndSample * 1000 / sampleRate)
            };
        }

        private static uint ReadBE32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
