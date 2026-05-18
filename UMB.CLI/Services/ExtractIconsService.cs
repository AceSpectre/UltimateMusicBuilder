using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Helpers;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Spectre.Console;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UMB.CLI.Services
{
    public class ExtractIconsService
    {
        private const string BntxPrefix = "series_0_";

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public ExtractIconsService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<ExtractIconsService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var ultimateTexCli = ToolPathResolver.Resolve(_musicConfig.CurrentValue.ToolsPath, "UltimateTexCli/ultimate_tex_cli");
            if (ultimateTexCli == null)
            {
                _logger.LogError("ultimate_tex_cli binary not found under Tools/UltimateTexCli. Run scripts/fetch-tools to install.");
                return;
            }

            var builtModPath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter path to built Sma5h mod folder (containing ui/replace/series/series_0/):")
                    .Validate(path =>
                    {
                        if (!Directory.Exists(path))
                            return ValidationResult.Error("Directory does not exist.");
                        var series0Dir = Path.Combine(path, "ui", "replace", "series", "series_0");
                        if (!Directory.Exists(series0Dir))
                            return ValidationResult.Error("No ui/replace/series/series_0/ folder found.");
                        return ValidationResult.Success();
                    }));

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            if (!Directory.Exists(modPath))
            {
                _logger.LogWarning("UMB mod path {ModPath} does not exist.", modPath);
                return;
            }

            var umbMods = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
            if (umbMods.Count == 0)
            {
                _logger.LogWarning("No UMB mod folders found under {ModPath}.", modPath);
                return;
            }

            var chosenModName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select the UMB mod to extract icons into:")
                    .PageSize(20)
                    .AddChoices(umbMods.Select(Path.GetFileName)));
            var chosenModPath = umbMods.First(p => string.Equals(Path.GetFileName(p), chosenModName, StringComparison.Ordinal));

            var series0Dir = Path.Combine(builtModPath, "ui", "replace", "series", "series_0");
            var bntxFiles = Directory.GetFiles(series0Dir, $"{BntxPrefix}*.bntx");
            if (bntxFiles.Length == 0)
            {
                _logger.LogWarning("No {Pattern}*.bntx files found in {Dir}.", BntxPrefix, series0Dir);
                return;
            }

            int totalExtracted = 0;

            foreach (var bntxFile in bntxFiles)
            {
                var seriesId = Path.GetFileNameWithoutExtension(bntxFile).Substring(BntxPrefix.Length);
                var targetSeriesDir = Path.Combine(chosenModPath, seriesId);

                if (!Directory.Exists(targetSeriesDir))
                {
                    _logger.LogWarning("No series folder '{SeriesId}' under '{ModName}', skipping.", seriesId, chosenModName);
                    continue;
                }

                var outputPng = Path.Combine(targetSeriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);

                if (File.Exists(outputPng))
                {
                    var overwrite = AnsiConsole.Confirm(
                        $"icon.png already exists for '{seriesId}'. Overwrite?",
                        defaultValue: false);
                    if (!overwrite)
                    {
                        _logger.LogInformation("Kept existing icon for '{SeriesId}'.", seriesId);
                        continue;
                    }
                }

                // ultimate_tex_cli (via image_dds) rejects BNTXs whose mipmap_count exceeds the
                // geometric max for their dimensions (floor(log2(maxDim))+1). Some Smash assets
                // (e.g. series icons) ship with an over-stated count. Patch a temp copy if so.
                string toolInput = bntxFile;
                string tempBntx = null;
                if (TryFixOverMipmapBntx(bntxFile, out var patchedBytes, out var oldMip, out var newMip))
                {
                    tempBntx = Path.Combine(Path.GetTempPath(),
                        $"umb_ext_{Guid.NewGuid():N}_{Path.GetFileName(bntxFile)}");
                    File.WriteAllBytes(tempBntx, patchedBytes);
                    toolInput = tempBntx;
                    _logger.LogInformation("Patched BNTX mipmap count {Old}ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢{New} for '{SeriesId}' (header inconsistent with dimensions).",
                        oldMip, newMip, seriesId);
                }

                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = ultimateTexCli,
                            Arguments = $"\"{toolInput}\" \"{outputPng}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && File.Exists(outputPng))
                    {
                        _logger.LogInformation("Extracted icon for '{SeriesId}' ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ {OutputPath}", seriesId, outputPng);
                        totalExtracted++;
                    }
                    else
                    {
                        var diagnostic = string.Join(" | ",
                            new[] { stderr, stdout }
                                .Select(s => s?.Trim())
                                .Where(s => !string.IsNullOrEmpty(s)));
                        if (string.IsNullOrEmpty(diagnostic))
                            diagnostic = $"exit code {process.ExitCode}";
                        _logger.LogError("Failed to extract icon for '{SeriesId}' from {BntxFile}: {Diagnostic}",
                            seriesId, bntxFile, diagnostic);
                    }
                }
                finally
                {
                    if (tempBntx != null && File.Exists(tempBntx))
                    {
                        try { File.Delete(tempBntx); } catch { /* best-effort cleanup */ }
                    }
                }
            }

            _logger.LogInformation("--------------------");
            if (totalExtracted > 0)
                _logger.LogInformation("Extracted {Count} icon(s).", totalExtracted);
            else
                _logger.LogInformation("No icons extracted.");
        }

        // BNTX ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ BRTI block: MipCount(u16) at +0x16, Width(i32) at +0x24, Height(i32) at +0x28.
        // Returns true if the first BRTI's mipmap_count exceeds floor(log2(max(w,h)))+1
        // (which is what image_dds enforces). The clamped bytes are returned via out; the
        // source file is never modified.
        private static bool TryFixOverMipmapBntx(string sourcePath, out byte[] patched, out int oldMip, out int newMip)
        {
            patched = null;
            oldMip = 0;
            newMip = 0;

            var bytes = File.ReadAllBytes(sourcePath);
            if (bytes.Length < 0x40 ||
                bytes[0] != (byte)'B' || bytes[1] != (byte)'N' ||
                bytes[2] != (byte)'T' || bytes[3] != (byte)'X')
                return false;

            int brti = -1;
            for (int i = 0x20; i <= bytes.Length - 0x30; i++)
            {
                if (bytes[i] == (byte)'B' && bytes[i + 1] == (byte)'R' &&
                    bytes[i + 2] == (byte)'T' && bytes[i + 3] == (byte)'I')
                {
                    brti = i;
                    break;
                }
            }
            if (brti < 0 || brti + 0x30 > bytes.Length)
                return false;

            int mipOffset = brti + 0x16;
            int mipCount = BitConverter.ToUInt16(bytes, mipOffset);
            int width = BitConverter.ToInt32(bytes, brti + 0x24);
            int height = BitConverter.ToInt32(bytes, brti + 0x28);

            if (width <= 0 || height <= 0 || mipCount <= 0)
                return false;

            int maxDim = Math.Max(width, height);
            int maxMipmaps = 0;
            for (int v = maxDim; v > 0; v >>= 1) maxMipmaps++;

            if (mipCount <= maxMipmaps)
                return false;

            oldMip = mipCount;
            newMip = maxMipmaps;
            var copy = (byte[])bytes.Clone();
            copy[mipOffset] = (byte)(maxMipmaps & 0xFF);
            copy[mipOffset + 1] = (byte)((maxMipmaps >> 8) & 0xFF);
            patched = copy;
            return true;
        }
    }
}
