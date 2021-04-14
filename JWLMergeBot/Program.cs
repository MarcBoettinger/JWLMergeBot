using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace JWLMergeBot
{
    public class Program
    {
        
        static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }
        
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .ConfigureLogging(logging =>
                {
                    // Settings for file logging
                    logging.AddFile(FileHandling.GetLogFilePath("Log-{Date}.txt"), retainedFileCountLimit: 365, levelOverrides: new Dictionary<string, LogLevel> {
                        { "Microsoft", LogLevel.Warning },
                        { "System", LogLevel.Warning }
                    });
                    
                    // Settings for console logging
                    logging.AddConsole();
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);
                })
                .ConfigureServices((hostContext, services) => services.AddHostedService<Worker>());
    }
}