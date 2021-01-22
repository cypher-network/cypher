// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using Autofac.Extensions.DependencyInjection;

using Serilog;
using Serilog.Events;

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
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("tgmnode.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();
            try
            {
                Log.Information("Starting web host");
                var builder = CreateWebHostBuilder(args);

                using (IHost host = builder.Build())
                {
                    host.Run();
                    host.WaitForShutdown();
                    Log.Information("Ran cleanup code inside using host block.");
                }
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
                webBuilder.UseStartup<Startup>().UseSerilog();
            });
    }
}
