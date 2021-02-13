// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using CYPCore.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CYPNode
{
    public static class Program
    {
        static IConfigurationRoot ConfigurationRoot
        {
            get
            {
                return new ConfigurationBuilder()
                                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                                .AddJsonFile("appsettings.json")
                                .Build();
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int Main(string[] args)
        {
            if (ConfigurationRoot.GetValue<bool>("Log:FirstChanceException"))
            {
                AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
                {
                    Log.Error(e.Exception, e.Exception.Message);
                };
            }

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(ConfigurationRoot, "Log")
                .CreateLogger();

            try
            {
                Log.Information("Starting web host");
                var builder = CreateWebHostBuilder(args);

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

        private static IHostBuilder CreateWebHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    ApiConfigurationOptions apiConfigurationOptions = new();
                    ConfigurationRoot.Bind("Api", apiConfigurationOptions);

                    webBuilder.UseStartup<Startup>()
                        .UseUrls(apiConfigurationOptions.Listening)
                        .UseSerilog();
                })

                // Use .NET Core system daemon support when applicable. This define can be set by setting the runtime
                // identifier (dotnet --runtime) to "linux_x64", "win-x64", etc. See cypnode.csproj for details.
#if BUILD_LINUX
                .UseSystemd();
#elif BUILD_MACOS
                ; // TODO: Check macOS support
#elif BUILD_WIN
                .UseWindowsService();
#else
                ;
#endif
    }
}
