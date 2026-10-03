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
        // Process exit codes, also reported in the daemon's __DONE__ line.
        private const int ExitOk = 0;
        private const int ExitFailed = 1;
        private const int ExitUsage = 2;

        async static Task<int> Main(string[] args)
        {
            CliOutput.Init();

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
                return ExitOk;
            }

            if (args.Length > 0)
            {
                using var scope = serviceProvider.CreateScope();
                return await RunAction(args[0].ToLowerInvariant(), scope.ServiceProvider, args.Length > 1 ? args[1..] : null);
            }

            while (true)
            {
                var action = ShowMenu(AnsiConsole.Console);
                if (action == "quit")
                    return ExitOk;

                using (var scope = serviceProvider.CreateScope())
                    await RunAction(action, scope.ServiceProvider);

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
        /// "__DONE__\t&lt;id&gt;\t&lt;code&gt;" line (code: 0 ok, 1 failed, 2 bad request)
        /// so the caller knows the result artifacts are fully written. Requests are
        /// processed serially.
        /// </summary>
        private static async Task RunDaemon(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                DaemonRequest req;
                try { req = JsonSerializer.Deserialize<DaemonRequest>(line, _daemonJsonOpts); }
                catch (JsonException) { req = null; }
                if (req == null)
                {
                    // Still answer, or the caller waits forever for this request's __DONE__.
                    logger.LogError("Ignoring malformed daemon request: {Request}", line);
                    CliOutput.WriteLine($"__DONE__\t0\t{ExitUsage}");
                    continue;
                }

                if (string.Equals(req.Action, "__shutdown__", StringComparison.OrdinalIgnoreCase))
                    return;

                int code;
                using (var scope = serviceProvider.CreateScope())
                    code = await RunAction(req.Action?.ToLowerInvariant() ?? "", scope.ServiceProvider, req.Args);

                CliOutput.WriteLine($"__DONE__\t{req.Id}\t{code}");
            }
        }

        /// <summary>
        /// Runs one action. Services report failure by logging an error, so the result is
        /// <see cref="ExitFailed"/> if the action threw or logged any error.
        /// </summary>
        private static async Task<int> RunAction(string action, IServiceProvider services, string[] extraArgs = null)
        {
            // Desktop actions skip Script, whose build services need the game resources set up.
            var desktop = services.GetRequiredService<Desktop.DesktopApi>();
            var entry = desktop.Handles(action) ? null : services.GetRequiredService<Script>();

            // Constructing services can log errors (e.g. StateManager without game resources);
            // only the action's own errors count.
            ErrorCountingLoggerProvider.Reset();
            try
            {
                if (entry == null)
                {
                    desktop.Run(action, extraArgs);
                    return ErrorCountingLoggerProvider.ErrorCount > 0 ? ExitFailed : ExitOk;
                }

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
                    case "accept-nus3":
                        entry.RunAcceptValidatedNus3();
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
                        Console.WriteLine("Usage: dotnet run [build|scaffold|convert|merge|extract-icons|nus3-convert|accept-nus3|cleanup|order-series|order-tracks|config-volume|config-volume-analyze|config-volume-save|config-volume-preview|dump-stages]");
                        return ExitUsage;
                }
            }
            catch (Exception ex)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program)).LogError(ex, "'{Action}' failed.", action);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]✗ '{action}' failed:[/] {ex.Message.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[dim]Full stack trace written to Log/log_*.txt[/]");
                AnsiConsole.WriteLine();
                return ExitFailed;
            }

            return ErrorCountingLoggerProvider.ErrorCount > 0 ? ExitFailed : ExitOk;
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

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter<ConsoleLoggerProvider>((ll) => ll >= LogLevel.Information)
                    .AddFile(Path.Combine(configuration.GetValue<string>("LogPath"), "log_{Date}.txt"), LogLevel.Debug, retainedFileCountLimit: 7)
                    .AddProvider(new ErrorCountingLoggerProvider());

                if (Console.IsOutputRedirected)
                    builder.AddProvider(new RedirectedConsoleLoggerProvider());
                else
                    builder.AddSimpleConsole((c) =>
                    {
                        c.SingleLine = true;
                    });
            });

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
            services.AddScoped<Services.ModsService>();
            services.AddScoped<Services.VanillaCatalogService>();
            services.AddScoped<Services.PlaylistAssignmentService>();
            services.AddScoped<Services.Nus3WorkflowService>();
            services.AddScoped<Desktop.DesktopApi>();
            services.AddScoped<Script>();
        }
    }
}
