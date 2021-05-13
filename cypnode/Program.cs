// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Autofac.Extensions.DependencyInjection;
using Serilog;
using CYPCore.Models;
using CYPCore.Helper;
using CYPNode.UI;

namespace CYPNode
{
    public static class Program
    {
        private const string AppSettingsFile = "appsettings.json";
        private const string AppSettingsFileDev = "appsettings.Development.json";

        /// <summary>
        ///
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int Main(string[] args)
        {
            if (args.FirstOrDefault(arg => arg == "--configuree") != null)
            {
                var ui = new TerminalUserInterface();
                var nc = new Configuration.Configuration(ui);
                return 0;
            }

            if (!File.Exists(AppSettingsFile))
            {
                Console.Error.WriteLine($"{AppSettingsFile} not found. Please create one running 'cypnode --configure'");
                return 1;
            }

            var config = new ConfigurationBuilder()

                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(AppSettingsFile, false)
                .AddJsonFile(AppSettingsFileDev, true)
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

            if (config.GetValue<bool>("Log:FirstChanceException"))
            {
                AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
                {
                    Log.Error(e.Exception, e.Exception.Message);
                };
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                Log.Error(ex, ex.Message);
            };

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

                host.Run();
                host.WaitForShutdown();
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
                    ApiConfigurationOptions apiConfigurationOptions = new();
                    configurationRoot.Bind("Api", apiConfigurationOptions);

                    webBuilder.UseStartup<Startup>()
                        .UseUrls(apiConfigurationOptions.Listening)
                        .UseSerilog();
                });
    }
}
