using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
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

            using (var scope = serviceProvider.CreateScope())
            {
                Script entry = scope.ServiceProvider.GetService<Script>();
                await entry.Run();
            }

            await Task.Delay(1000);
            Console.WriteLine("The program has completed its task. Press enter to exit");
            Console.ReadLine();
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
