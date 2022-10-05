// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Autofac.Extensions.DependencyInjection;
using Serilog;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetworkNode.Configuration;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace CypherNetworkNode;

public static class Program
{
    private const string AppSettingsFile = "appsettings.json";

    /// <summary>
    ///
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(AppSettingsFile, false, true)
            .AddCommandLine(args)
            .Build();

        // args = new string[] { "--configure", "--showkey" };
        // args = new string[] { "--configure" };
        var settingsExists = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSettingsFile));
        if (!settingsExists)
        {
            await Console.Error.WriteLineAsync($"{AppSettingsFile} not found.");
            return 1;
        }
        if (args.FirstOrDefault(arg => arg == "--configure") != null)
        {
            var commands = args.Where(x => x != "--configure").ToArray();
            if (commands.Contains("--showkey"))
            {
                Startup.ShowPrivateKey = true;
            }
            else
            {
                var _ = new Utility(config.Get<Config>());
                return 0;
            }
        }

        const string logSectionName = "Log";
        if (config.GetSection(logSectionName) != null)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config, logSectionName)
                .CreateLogger();
        }
        else
        {
            throw new Exception($"No \"{logSectionName}\" section found in appsettings.json");
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine(@$"
   ______               __                                      __        
  / ____/__  __ ____   / /_   ___   _____ ____   __  __ ____   / /__ _____
 / /    / / / // __ \ / __ \ / _ \ / ___// __ \ / / / // __ \ / //_// ___/
/ /___ / /_/ // /_/ // / / //  __// /   / /_/ // /_/ // / / // ,<  (__  ) 
\____/ \__, // .___//_/ /_/ \___//_/   / .___/ \__,_//_/ /_//_/|_|/____/  
      /____//_/                       /_/         write code: v{Util.GetAssemblyVersion()} RC1");

            Console.WriteLine();
            Console.ResetColor();
            Log.Information("Starting Cypher...");
            Log.Information("Process ID: {@Message}", Environment.ProcessId);
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
            Log.Information("Ran cleanup code inside using host block");
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

    /// <summary>
    /// 
    /// </summary>
    /// <param name="args"></param>
    /// <param name="configurationRoot"></param>
    /// <returns></returns>
    private static IHostBuilder CreateWebHostBuilder(string[] args, IConfigurationRoot configurationRoot) => Host
        .CreateDefaultBuilder(args).UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<Startup>().UseKestrel(options =>
            {
                var ipAddress = Util.GetIpAddress();
                var port = Convert.ToInt32(configurationRoot["Node:Network:HttpPort"]);
                var endPoint = new IPEndPoint(ipAddress, port);
                options.Listen(ipAddress, port);
                Log.Information("Http Listening on {Endpoint}", endPoint);
                var certMode = configurationRoot["Node:Network:CertificateMode"];
                if (certMode != "self") return;
                Log.Information("Certificate Mode: self");
                options.Listen(ipAddress, Convert.ToInt32(configurationRoot["Node:Network:HttpsPort"]), listenOptions =>
                {
                    if (!string.IsNullOrEmpty(configurationRoot["Node:Network:X509Certificate:CertPath"]) &&
                        !string.IsNullOrEmpty(configurationRoot["Node:Network:X509Certificate:Password"]))
                    {
                        listenOptions.UseHttps(
                            Path.Combine(Util.EntryAssemblyPath(),
                                configurationRoot["Node:Network:X509Certificate:CertPath"]),
                            configurationRoot["Node:Network:X509Certificate:Password"]);
                    }
                    else
                    {
                        listenOptions.UseHttps(
                            new CertificateResolver().ResolveCertificate(
                                configurationRoot["Node:Network:X509Certificate:Thumbprint"])!);
                    }
                });
            });
        }).UseSerilog();
}