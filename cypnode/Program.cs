// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Autofac.Extensions.DependencyInjection;
using Serilog;
using CYPCore.Models;
using CYPCore.Helper;
using CYPNode.Setup;

namespace CYPNode
{
    public static class Program
    {
        public const string AppSettingsFile = "appsettings.json";
        private const string AppSettingsFileDev = "appsettings.Development.json";

        /// <summary>
        ///
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public async static Task<int> Main(string[] args)
        {
            var appsettingsExists = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSettingsFile));
            if (args.FirstOrDefault(arg => arg == "--configure") != null)
            {
                var commands = args.Where(x => x != "--configure");
                var configSettings = new Config();
                return configSettings.Init(commands.ToArray());
            }

            if (!appsettingsExists)
            {
                Console.Error.WriteLine($"{AppSettingsFile} not found.");
                return 1;
            }

            var config = new ConfigurationBuilder()

                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(AppSettingsFile, false,true)
                .AddCommandLine(args)
                .Build();

            const string logSectionName = "Log";
            if (config.GetSection(logSectionName) != null)
            {
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(config, logSectionName)
                    .CreateLogger();
            }
            else
            {
                throw new Exception(string.Format($"No \"{@logSectionName}\" section found in appsettings.json", logSectionName));
            }
            
            try
            {
                Log.Information("Starting web host");
                Log.Information($"Version: {Util.GetAssemblyVersion()}");
                var builder = CreateWebHostBuilder(args, config);

                var platform = Util.GetOperatingSystemPlatform();
                if (platform == OSPlatform.Linux)
                {
                    builder.UseSystemd();
                }
                else if (platform == OSPlatform.OSX)
                {
                    // TODO
                }
                else if (platform == OSPlatform.Windows)
                {
                    builder.UseWindowsService();
                }

                using var host = builder.Build();

                await host.RunAsync();
                await host.WaitForShutdownAsync();
                Log.Information("Ran cleanup code inside using host block.");
            }
            catch (ObjectDisposedException)
            {
                // TODO: Check if ObjectDisposedException can occur given the above implementation. Either remove, add log output or a comment for the reason of this occurence.
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }

            return 0;
        }

        private static IHostBuilder CreateWebHostBuilder(string[] args, IConfigurationRoot configurationRoot) =>
            Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    AppOptions appConfigurationOptions = new();
                    configurationRoot.Bind("Node", appConfigurationOptions);

                    webBuilder.UseStartup<Startup>()
                        .UseUrls(appConfigurationOptions.RestApi)
                        .UseSerilog();
                });
    }
}
