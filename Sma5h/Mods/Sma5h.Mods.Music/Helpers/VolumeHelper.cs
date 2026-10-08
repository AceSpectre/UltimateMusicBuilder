using System;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>nus3bank volume is dB; user-facing volumes are linear multipliers.</summary>
    public static class VolumeHelper
    {
        private const float MUTED_NUS3BANK_VOLUME_DB = -90f;

        public static float MultiplierToDb(float multiplier) =>
            multiplier > 0 ? (float)(20 * Math.Log10(multiplier)) : MUTED_NUS3BANK_VOLUME_DB;

        public static float DbToMultiplier(float db) => (float)Math.Pow(10, db / 20);
    }
}
