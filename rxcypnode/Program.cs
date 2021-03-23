using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using rxcypcore.Models;
using rxcypnode.UI;
using Serilog;

namespace rxcypnode
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //if (args.FirstOrDefault(arg => arg == "-configure") != null)
            {
                var ui = new TerminalUserInterface();
                var nc = new Configuration.Configuration(ui);
                return;
            }
            
            var settingsFile = $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json";
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(settingsFile, optional: false)
                .AddCommandLine(args)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config, "Logging")
                .CreateLogger();
            
            var host = CreateHostBuilder(args, config).Build();
            host.Run();
            host.WaitForShutdown();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, IConfigurationRoot configurationRoot) =>
            Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    ApiConfigurationOptions apiConfigurationOptions = new();
                    configurationRoot.Bind("Api", apiConfigurationOptions);

                    webBuilder.UseStartup<Startup>()
                        .UseUrls(apiConfigurationOptions.Listening)
                        .UseSerilog();
                });
    }
}