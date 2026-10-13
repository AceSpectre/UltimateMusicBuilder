namespace Sma5h.Mods.Music.Models
{
    /// <summary>The playlist, and its order slot (0-15), a stage plays.</summary>
    public record StagePlaylistAssignment(string UiStageId, string BgmSetId, byte BgmSettingNo);
}
