FFmpeg is required for two UltimateMusicBuilder features:

  1. LUFS-based loudness normalization at build time
     (Sma5hMusic.LufsNormalization in appsettings.json — enabled by default)

  2. The "Config Volume" preview window
     (audio playback of tracks at their post-build loudness)

Drop ffmpeg.exe into this folder.

A static Windows build of FFmpeg can be obtained from:

  https://www.gyan.dev/ffmpeg/builds/        (gyan.dev — recommended for Windows)
  https://github.com/BtbN/FFmpeg-Builds/releases   (BtbN)

The "essentials" build is sufficient; the "full" build also works.

Licensing
---------
FFmpeg is licensed under the LGPL by default, with optional GPL components.
If you ship a build of UltimateMusicBuilder that includes ffmpeg.exe, include
the license text accompanying the FFmpeg binary you bundled. See
https://ffmpeg.org/legal.html for details.

Verifying
---------
The expected default location is configured in appsettings.json under
Sma5hMusic.LufsNormalization.FfmpegPath. Adjust that path if you put FFmpeg
elsewhere.

If ffmpeg.exe is missing, UltimateMusicBuilder logs a single warning at the
start of the build and falls back to legacy behavior (the tracks.csv `volume`
column is written to the bank file without any LUFS adjustment).
