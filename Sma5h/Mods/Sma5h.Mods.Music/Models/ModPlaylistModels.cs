namespace Sma5h.Mods.Music.Models
{
    /// <summary>The playlist, and its order slot (0-15), a stage plays.</summary>
    public record StagePlaylistAssignment(string UiStageId, string BgmSetId, byte BgmSettingNo);

    /// <summary>A song the build keeps in a playlist, added with this incidence when missing.</summary>
    public record RequiredSong(string PlaylistId, string UiBgmId, ushort Incidence);
}
