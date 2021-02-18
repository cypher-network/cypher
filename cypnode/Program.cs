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

namespace CYPNode
{
    public static class Program
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int Main(string[] args)
        {
            var configFile = args.SkipWhile(arg => !arg.Equals("--config")).Skip(1).FirstOrDefault();
            if (string.IsNullOrEmpty(configFile))
            {
                // No config file set, check in current path (priority over system default)
                configFile = Util.ConfigurationFile.Local();

                // If no config file in current path, use system default
                if (!File.Exists(configFile))
                {
                    configFile = Util.ConfigurationFile.SystemDefault();
                }
            }

            IConfigurationRoot configurationRoot = null;
            if (File.Exists(configFile))
            {
                configurationRoot = new ConfigurationBuilder()
                    .AddJsonFile(configFile)
                    .Build();
            }
            else
            {
                throw new FileNotFoundException($"Cannot find configuration file {@configFile}", configFile);
            }

            if (configurationRoot.GetValue<bool>("Log:FirstChanceException"))
            {
                AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
                {
                    Log.Error(e.Exception, e.Exception.Message);
                };
            }

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configurationRoot, "Log")
                .CreateLogger();

            Log.Information("Using application configuration from {@configFile}", configFile);

            try
            {
                Log.Information("Starting web host");
                var builder = CreateWebHostBuilder(args, configurationRoot);

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

                using IHost host = builder.Build();

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
