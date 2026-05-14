namespace Sma5h.Mods.Music.Interfaces
{
    public interface ILufsAnalysisService
    {
        LufsMeasurement Measure(string audioFilePath);
        GainResult CalculateGain(LufsMeasurement measurement, float targetLufs, float maxMultiplier);
        bool IsAvailable { get; }
        /// <summary>
        /// The resolved FFmpeg executable path (configured path if it exists on disk,
        /// otherwise "ffmpeg" if found on PATH, or null if neither is available).
        /// Callers can pass this directly to Process.Start.
        /// </summary>
        string ResolvedFfmpegPath { get; }
        void SaveCache();
    }

    public class LufsMeasurement
    {
        public float IntegratedLufs { get; set; }
        public float TruePeakDb { get; set; }
        public float LoudnessRangeLu { get; set; }
        public bool IsValid { get; set; }
    }

    public readonly struct GainResult
    {
        public float Multiplier { get; }
        public bool WasClamped { get; }

        public GainResult(float multiplier, bool wasClamped)
        {
            Multiplier = multiplier;
            WasClamped = wasClamped;
        }
    }
}
