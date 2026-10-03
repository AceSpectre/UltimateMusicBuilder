using System.Collections.Generic;
using System.Text.Json.Serialization;

// Mirrors UMB.Desktop/src/shared/types.ts — the JSON contract with the desktop app.
namespace UMB.CLI.Desktop
{
    // ── Mods ───────────────────────────────────────────────────────────────
    public record ModInfo(string Name, string Path);
    public record ModStats(int SeriesCount, int TrackCount);

    // ── Series order ───────────────────────────────────────────────────────
    public record SeriesGame(string Id, string Name);

    public class SeriesFields
    {
        public string Name { get; set; } = "";
        public string SeriesPlaylist { get; set; } = "";
        public int PlaylistIncidence { get; set; } = 100;
        public List<SeriesGame> Games { get; set; } = new();
        public string DefaultGame { get; set; } = "";
        public string DefaultAuthor { get; set; } = "";
        public string DefaultCopyright { get; set; } = "";
        public string DefaultRecordType { get; set; } = "original";
        public double DefaultVolume { get; set; } = 1;
    }

    public record SeriesOrderItem(string Id, string Name, string SeriesId, string IconDataUrl, int OriginalIndex, SeriesFields Fields, bool IsExistingSeries = false);
    public record SeriesOrderData(string ModName, string ModPath, bool HasSeriesOrder, List<SeriesOrderItem> Items);
    public record SaveSeriesItem(string Id, SeriesFields Fields);

    public class CreateSeriesInput
    {
        public string SeriesId { get; set; } = "";
        public string Name { get; set; } = "";
        public string SeriesPlaylist { get; set; } = "";
        public List<SeriesGame> Games { get; set; } = new();
        public string IconDataUrl { get; set; }
    }

    // ── Track order ────────────────────────────────────────────────────────
    public class TrackFields
    {
        public string Title { get; set; } = "";
        public string Game { get; set; } = "";
        public string Author { get; set; } = "";
        public string Copyright { get; set; } = "";
        [JsonPropertyName("record_type")] public string RecordType { get; set; } = "original";
        [JsonPropertyName("special_category")] public string SpecialCategory { get; set; } = "";
        public string Info1 { get; set; } = "";
        [JsonPropertyName("in_soundtest")] public string InSoundtest { get; set; } = "True";
    }

    public record TrackOrderItem(string Id, string Title, string Subtitle, string BgmId, string Filename,
        bool IsLocked, int? OriginalIndex, TrackFields Fields, bool IsPinchTarget);

    public record VanillaSongOption(string InfoId, string Name);

    public class DefaultTrackData
    {
        public string Game { get; set; } = "";
        public string Author { get; set; } = "";
        public string Copyright { get; set; } = "";
        [JsonPropertyName("record_type")] public string RecordType { get; set; } = "original";
    }

    public record TrackOrderData(string SeriesName, string SeriesPath, bool IsExistingSeries, bool HasSongOrder,
        List<SeriesGame> Games, List<VanillaSongOption> VanillaSongs, DefaultTrackData DefaultTrackData, List<TrackOrderItem> Items);

    public record SaveTrackItem(string Id, TrackFields Fields);

    // ── Merge ──────────────────────────────────────────────────────────────
    public record MergeSeriesSource(string ModName, string ModPath, string SeriesPath);
    public record MergeSeries(string Name, List<MergeSeriesSource> Sources);
    public record MergeConflict(string SeriesName, List<string> Mods);
    public record MergeAnalysis(List<string> ModNames, List<string> ModPaths, List<MergeSeries> Series, List<MergeConflict> Conflicts, int TotalSeries);
    public record MergeResult(string OutputPath, string OutputName, int TotalSeries, int TotalTracks, int ConflictsResolved);

    // ── Extract icons ──────────────────────────────────────────────────────
    public record ExtractIconMatch(string SeriesId, string BntxPath, bool HasExistingIcon);
    public record ExtractIconsAnalysis(string CompiledModPath, string ModPath, string ModName, List<ExtractIconMatch> Matched, List<string> Unmatched);
    public record ExtractIconsResult(int Extracted, int Skipped, int Failed);

    // ── Nus3 convert ───────────────────────────────────────────────────────
    public class LoopCandidate
    {
        public int Rank { get; set; }
        public double Score { get; set; }
        public double LoopStart { get; set; }
        public double LoopEnd { get; set; }
        public double LoopLength { get; set; }
        public string LoopStartStr { get; set; } = "";
        public string LoopEndStr { get; set; } = "";
        public string LoopLengthStr { get; set; } = "";
        public bool BeatAligned { get; set; }
        public int? Bars { get; set; }
        public double Tempo { get; set; }
        public string Key { get; set; } = "";
        public double NoteDistance { get; set; }
        public double SpectralSim { get; set; }
        public double RmsDelta { get; set; }
        public string Seam { get; set; } = "smooth";
        public string Note { get; set; } = "";
    }

    public record Nus3SourceTrack(string Id, string Name, string Src, string Duration, double DurationSeconds, bool Converted);
    public record Nus3AnalysisResult(Nus3SourceTrack Track, List<LoopCandidate> Candidates);

    public class Nus3ConversionMeta
    {
        public string Mode { get; set; } = "loop";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LoopCandidate Candidate { get; set; }
    }

    public class LoopAnalysisOptions
    {
        public double? MinLoopDuration { get; set; }
        public double? MinDurationMultiplier { get; set; }
        public bool? DisablePruning { get; set; }
        public bool? Force { get; set; }
    }

    // ── Playlist info ──────────────────────────────────────────────────────
    public record PlaylistInfo(string Id, string Name, List<string> Series, int SongCount);
    public record StageSong(int Order, string BgmId, string Name);
    public record StageInfo(string UiStageId, string Name, bool Hidden, string SeriesId, string SeriesName,
        string PlaylistId, string PlaylistName, int Order, List<StageSong> Songs);
    public record PlaylistInfoData(List<PlaylistInfo> Playlists, List<StageInfo> Stages);

    // ── Manage playlists ───────────────────────────────────────────────────
    public record PlaylistSongAssignment(string SeriesId, string SeriesName, string Filename, string Title, int Incidence);
    public record PlaylistTarget(string Id, string Name, int AssignedCount, List<PlaylistSongAssignment> Assignments);
    public record ModSong(string SeriesId, string SeriesName, string Filename, string Title);
    public record ManagePlaylistsData(string ModName, string ModPath, List<PlaylistTarget> Playlists, List<ModSong> Songs);
    public record PlaylistAssignmentInput(string PlaylistId, string SeriesId, string Filename, double Incidence);
}
