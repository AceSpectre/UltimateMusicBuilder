using System;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>
    /// The nus3bank volume the game reads is in dB (vanilla songs range -12..12, -90 = muted),
    /// while the user-facing settings (tracks.csv volume, GlobalVolumeMultiplier, LUFS gain) are
    /// linear multipliers. Convert them here before adding them to a bank volume.
    /// </summary>
    public static class VolumeHelper
    {
        public const float MUTED_NUS3BANK_VOLUME_DB = -90f;

        public static float MultiplierToDb(float multiplier)
        {
            if (multiplier <= 0 || float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return MUTED_NUS3BANK_VOLUME_DB;
            return (float)(20 * Math.Log10(multiplier));
        }
    }
}
