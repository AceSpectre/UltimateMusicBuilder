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
                default:
                    Console.WriteLine($"Unknown command: {action}");
                    Console.WriteLine("Usage: dotnet run [build|scaffold|convert|extract-icons]");
                    break;
            }
        }

        private static readonly Dictionary<string, string> MenuOptions = new()
        {
            ["Build          - Build mods and generate ArcOutput"] = "build",
            ["Scaffold       - Create series.toml/tracks.csv and populate new music files"] = "scaffold",
            ["Convert        - Import a Sma5h mod to UMB folder format"] = "convert",
            ["Extract Icons  - Extract series icons from a built Sma5h mod"] = "extract-icons",
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
            services.AddScoped<Script>();

            services.AddLogging();
        }
    }
}
