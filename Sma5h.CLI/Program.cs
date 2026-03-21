using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Sma5h.CLI
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
                default:
                    Console.WriteLine($"Unknown command: {action}");
                    Console.WriteLine("Usage: dotnet run [build|scaffold|convert|extract-icons|nus3-convert|accept-nus3|cleanup]");
                    break;
            }
        }

        private static readonly Dictionary<string, string> MenuOptions = new()
        {
            ["Build          - Build mods and generate ArcOutput"] = "build",
            ["Scaffold       - Create series.toml/tracks.csv and populate new music files"] = "scaffold",
            ["Nus3 Convert   - Convert audio files to nus3audio with loop points"] = "nus3-convert",
            ["Accept Nus3    - Accept validated nus3audio files into series"] = "accept-nus3",
            ["Convert        - Import a Sma5h mod to UMB folder format"] = "convert",
            ["Extract Icons  - Extract series icons from a built Sma5h mod"] = "extract-icons",
            ["Cleanup        - Remove tracks.csv entries for missing audio files"] = "cleanup",
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
            services.AddScoped<Services.ExtractIconsService>();
            services.AddScoped<Services.Nus3ConvertService>();
            services.AddScoped<Services.AcceptNus3Service>();
            services.AddScoped<Services.CleanupService>();
            services.AddScoped<Script>();

            services.AddLogging();
        }
    }
}
