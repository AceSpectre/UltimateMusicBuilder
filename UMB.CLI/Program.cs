using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;

namespace UMB.CLI
{
    class Program
    {
        async static Task Main(string[] args)
        {
            // VGAudioCli is a loose managed .exe in Tools/, not a NuGet package; a single-file publish neither bundles it nor lists it in .deps.json, so resolve it by hand.
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                if (!string.Equals(name.Name, "VGAudioCli", StringComparison.OrdinalIgnoreCase))
                    return null;
                foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    var candidate = Path.Combine(root, "Tools", "VGAudioCli.exe");
                    if (File.Exists(candidate))
                        return ctx.LoadFromAssemblyPath(candidate);
                }
                return null;
            };

            // Resolve the working directory that relative paths (Mods/, Resources/, Tools/,
            // ArcOutput/, …) are resolved against. Priority:
            //   1. UMB_WORKSPACE env var — explicit override used by the desktop app and
            //      power users to point UMB at any folder.
            //   2. The launch CWD, if it already looks like a workspace (has Resources/).
            //      Lets a release build run straight from a populated folder.
            //   3. Walk up from the executable's directory to find Resources/ (dev: repo root).
            var envWorkspace = Environment.GetEnvironmentVariable("UMB_WORKSPACE");
            if (!string.IsNullOrWhiteSpace(envWorkspace) && Directory.Exists(envWorkspace))
            {
                Directory.SetCurrentDirectory(Path.GetFullPath(envWorkspace));
            }
            else if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Resources")))
            {
                var appRoot = new DirectoryInfo(AppContext.BaseDirectory);
                while (appRoot != null && !Directory.Exists(Path.Combine(appRoot.FullName, "Resources")))
                    appRoot = appRoot.Parent;
                if (appRoot != null)
                    Directory.SetCurrentDirectory(appRoot.FullName);
            }

            var services = new ServiceCollection();
            ConfigureServices(services, args);
            var serviceProvider = services.BuildServiceProvider();

            // Persistent daemon mode: keep one process alive and service headless
            // batch requests over stdin. Avoids paying process-spawn + DI bootstrap
            // on every desktop call (e.g. switching series in Config Volume).
            if (args.Length > 0 && args[0].Equals("serve", StringComparison.OrdinalIgnoreCase))
            {
                await RunDaemon(serviceProvider);
                return;
            }

            if (args.Length > 0)
            {
                using var scope = serviceProvider.CreateScope();
                var entry = scope.ServiceProvider.GetService<Script>();
                await RunAction(args[0].ToLowerInvariant(), entry, args.Length > 1 ? args[1..] : null);
                return;
            }

            while (true)
            {
                var action = ShowMenu(AnsiConsole.Console);
                if (action == "quit")
                    return;

                using (var scope = serviceProvider.CreateScope())
                {
                    var entry = scope.ServiceProvider.GetService<Script>();
                    await RunAction(action, entry);
                }

                AnsiConsole.WriteLine();
            }
        }

        private static readonly JsonSerializerOptions _daemonJsonOpts = new() { PropertyNameCaseInsensitive = true };

        private class DaemonRequest
        {
            public int Id { get; set; }
            public string Action { get; set; }
            public string[] Args { get; set; }
        }

        /// <summary>
        /// Reads newline-delimited JSON requests from stdin, runs each action against
        /// a fresh DI scope (reusing the already-built service provider — and the
        /// singleton LUFS cache stays warm across requests), then prints a sentinel
        /// "__DONE__\t&lt;id&gt;\t&lt;code&gt;" line so the caller knows the result
        /// artifacts (output files) are fully written. Requests are processed serially.
        /// </summary>
        private static async Task RunDaemon(IServiceProvider serviceProvider)
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                DaemonRequest req;
                try { req = JsonSerializer.Deserialize<DaemonRequest>(line, _daemonJsonOpts); }
                catch { continue; }
                if (req == null) continue;

                if (string.Equals(req.Action, "__shutdown__", StringComparison.OrdinalIgnoreCase))
                    return;

                // RunAction catches and logs handler exceptions (same as one-shot mode,
                // which also exits 0 on a caught failure), so we always report code 0.
                using (var scope = serviceProvider.CreateScope())
                {
                    var entry = scope.ServiceProvider.GetService<Script>();
                    await RunAction(req.Action?.ToLowerInvariant() ?? "", entry, req.Args);
                }

                Console.Out.WriteLine($"__DONE__\t{req.Id}\t0");
                Console.Out.Flush();
            }
        }

        private static async Task RunAction(string action, Script entry, string[] extraArgs = null)
        {
            try
            {
                switch (action)
                {
                    case "build":
                        await entry.RunBuild(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "scaffold":
                        entry.RunScaffold();
                        break;
                    case "convert":
                        entry.RunConvert(
                            extraArgs?.Length > 0 ? extraArgs[0] : null,
                            extraArgs?.Length > 1 ? extraArgs[1] : null);
                        break;
                    case "merge":
                        entry.RunMerge();
                        break;
                    case "extract-icons":
                        entry.RunExtractIcons();
                        break;
                    case "nus3-convert":
                        entry.RunNus3Convert();
                        break;
                    case "nus3-convert-batch":
                        entry.RunNus3ConvertBatch(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "accept-nus3":
                        entry.RunAcceptValidatedNus3();
                        break;
                    case "accept-nus3-batch":
                        entry.RunAcceptValidatedNus3Batch(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "cleanup":
                        entry.RunCleanup();
                        break;
                    case "order-series":
                        entry.RunOrderSeries();
                        break;
                    case "order-tracks":
                        entry.RunOrderTracks();
                        break;
                    case "config-volume":
                        entry.RunConfigVolume();
                        break;
                    case "config-volume-analyze":
                        entry.RunConfigVolumeAnalyze(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "config-volume-save":
                        entry.RunConfigVolumeSave(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "config-volume-preview":
                        entry.RunConfigVolumePreview(extraArgs?.Length > 0 ? extraArgs[0] : null);
                        break;
                    case "dump-stages":
                        entry.RunDumpStages();
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {action}");
                        Console.WriteLine("Usage: dotnet run [build|scaffold|convert|merge|extract-icons|nus3-convert|nus3-convert-batch|accept-nus3|accept-nus3-batch|cleanup|order-series|order-tracks|config-volume|config-volume-analyze|config-volume-save|config-volume-preview|dump-stages]");
                        break;
                }
            }
            catch (Exception ex)
            {
                entry.Logger.LogError(ex, "'{Action}' failed.", action);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]✗ '{action}' failed:[/] {ex.Message.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[dim]Full stack trace written to Log/log_*.txt[/]");
                AnsiConsole.WriteLine();
            }
        }

        internal static readonly Dictionary<string, string> MenuOptions = new()
        {
            ["Build          - Build mods and generate ArcOutput"] = "build",
            ["Scaffold       - Create series.toml/tracks.csv and populate new music files"] = "scaffold",
            ["Nus3 Convert   - Convert audio files to nus3audio with loop points"] = "nus3-convert",
            ["Accept Nus3    - Accept validated nus3audio files into series"] = "accept-nus3",
            ["Convert        - Import a Sma5h mod to UMB folder format"] = "convert",
            ["Merge          - Merge two or more UMB mods into one"] = "merge",
            ["Extract Icons  - Extract series icons from a built Sma5h mod"] = "extract-icons",
            ["Cleanup        - Remove tracks.csv entries for missing audio files"] = "cleanup",
            ["Order Series   - Reorder custom series display order via drag-and-drop"] = "order-series",
            ["Order Tracks   - Reorder tracks within a series via drag-and-drop"] = "order-tracks",
            ["Config Volume  - Preview tracks at post-build loudness, override per-track gain"] = "config-volume",
            ["Dump Stages    - Print every stage's BgmSetId mapping (diagnostic)"] = "dump-stages",
            ["Quit"] = "quit",
        };

        internal static string ShowMenu(IAnsiConsole console)
        {
            console.MarkupLine("[bold]Sma5h Music Mod Builder[/]");
            console.WriteLine();

            var choice = console.Prompt(
                new SelectionPrompt<string>()
                    .WrapAround()
                    .Title("Select an action:")
                    .HighlightStyle(new Style(Color.Cyan1))
                    .AddChoices(MenuOptions.Keys));

            return MenuOptions[choice];
        }

        private static void ConfigureServices(IServiceCollection services, string[] args)
        {
            var configuration = new ConfigurationBuilder()
               .SetBasePath(AppContext.BaseDirectory)
               .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
               .AddCommandLine(args)
               .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder
                .AddFilter<ConsoleLoggerProvider>((ll) => ll >= LogLevel.Information)
                .AddFile(Path.Combine(configuration.GetValue<string>("LogPath"), "log_{Date}.txt"), LogLevel.Debug, retainedFileCountLimit: 7)
                .AddSimpleConsole((c) =>
                {
                    c.SingleLine = true;
                }));

            services.AddLogging();
            services.AddOptions();
            services.AddSingleton(configuration);
            services.AddSingleton(loggerFactory);

            services.AddSma5hCore(configuration);
            services.AddSma5hMusic(configuration);

            services.AddScoped<IWorkspaceManager, WorkspaceManager>();
            services.AddScoped<Services.BuildService>();
            services.AddScoped<Services.ScaffoldService>();
            services.AddScoped<Services.ConvertService>();
            services.AddScoped<Services.MergeService>();
            services.AddScoped<Services.ExtractIconsService>();
            services.AddScoped<Services.Nus3ConvertService>();
            services.AddScoped<Services.AcceptNus3Service>();
            services.AddScoped<Services.CleanupService>();
            services.AddScoped<Services.SeriesOrderService>();
            services.AddScoped<Services.TrackOrderService>();
            services.AddScoped<Services.VolumeConfigService>();
            services.AddScoped<Services.DumpStagesService>();
            services.AddScoped<Script>();
        }
    }
}
