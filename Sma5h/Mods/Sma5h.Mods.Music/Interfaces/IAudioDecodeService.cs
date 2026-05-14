namespace Sma5h.Mods.Music.Interfaces
{
    public interface IAudioDecodeService
    {
        /// <summary>
        /// Decodes a Smash-compatible audio file (.nus3audio, .lopus, .idsp, .brstm)
        /// or a standard format (.wav, .mp3, .ogg, .flac) to a PCM WAV at the given
        /// output path. Returns true on success. The caller owns the output file
        /// and is responsible for deleting it.
        ///
        /// For .wav inputs this is a no-op that copies the file (so callers can
        /// uniformly clean up).
        /// </summary>
        bool DecodeToWav(string inputFile, string outputWav);
    }
}
