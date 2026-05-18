using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace UMB.CLI
{
    class Program
    {
        async static Task Main(string[] args)
        {
            var appRoot = new DirectoryInfo(AppContext.BaseDirectory);
            while (appRoot != null && !Directory.Exists(Path.Combine(appRoot.FullName, "Resources")))
                appRoot = appRoot.Parent;
            if (appRoot != null)
                Directory.SetCurrentDirectory(appRoot.FullName);

            var services = new ServiceCollection();
            ConfigureServices(services, args);
            var serviceProvider = services.BuildServiceProvider();

            // If args provided, run once and exit
            if (args.Length > 0)
            {
                using var scope = serviceProvider.CreateScope();
                var entry = scope.ServiceProvider.GetService<Script>();
                await RunAction(args[0].ToLowerInvariant(), entry);
                return;
            }

            // Interactive loop
            while (true)
            {
                var action = ShowMenu();
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

        private static async Task RunAction(string action, Script entry)
        {
            try
            {
                switch (action)
                {
                    case "build":
                        await entry.RunBuild();
                        break;
                    case "scaffold":
                        entry.RunScaffold();
                        break;
                    case "convert":
                        entry.RunConvert();
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
                    case "dump-stages":
                        entry.RunDumpStages();
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {action}");
                        Console.WriteLine("Usage: dotnet run [build|scaffold|convert|merge|extract-icons|nus3-convert|accept-nus3|cleanup|order-series|order-tracks|config-volume|dump-stages]");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Write the full stack trace to the rolling log file so the cause is recoverable,
                // and print a concise, color-coded message to the console so the user sees
                // exactly what's missing without scrolling past a wall of stack frames.
                entry.Logger.LogError(ex, "'{Action}' failed.", action);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]✗ '{action}' failed:[/] {ex.Message.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[dim]Full stack trace written to Log/log_*.txt[/]");
                AnsiConsole.WriteLine();
            }
        }

        private static readonly Dictionary<string, string> MenuOptions = new()
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
            ["Config Volume  - Preview tracks at post-build loudness & override per-track normalization gain"] = "config-volume",
            ["Dump Stages    - Print every stage's BgmSetId mapping (diagnostic)"] = "dump-stages",
            ["Quit"] = "quit",
        };

        private static string ShowMenu()
        {
            AnsiConsole.MarkupLine("[bold]Sma5h Music Mod Builder[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
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

            //Sma5h Core
            services.AddSma5hCore(configuration);
            services.AddSma5hMusic(configuration);

            //CLI
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

            services.AddLogging();
        }
    }
}
