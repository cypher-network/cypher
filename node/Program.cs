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
using CypherNetworkNode.UI;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace CypherNetworkNode;

public static class Program
{
    public const string AppSettingsFile = "appsettings.json";

    /// <summary>
    ///
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public static async Task<int> Main(string[] args)
    {
        //args = new string[] { "--configure", "--help" };
        var settingsExists = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSettingsFile));
        if (args.FirstOrDefault(arg => arg == "--configure") != null)
        {
            var commands = args.Where(x => x != "--configure").ToArray();
            if (commands.Contains("--showkey"))
            {
                Startup.ShowPrivateKey = true;
            }
            else
            {
                var ui = new TerminalUserInterface();
                var nc = new Configuration.Configuration(ui);
                return 0;
            }
        }

        if (!settingsExists)
        {
            await Console.Error.WriteLineAsync($"{AppSettingsFile} not found.");
            return 1;
        }

        var config = new ConfigurationBuilder()

            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(AppSettingsFile, false, true)
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
            throw new Exception($"No \"{logSectionName}\" section found in appsettings.json");
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(@$"
▄▄███▄▄· ██╗ ██████╗██╗   ██╗██████╗ ██╗ 
██╔════╝██╔╝██╔════╝╚██╗ ██╔╝██╔══██╗╚██╗
███████╗██║ ██║      ╚████╔╝ ██████╔╝ ██║
╚════██║██║ ██║       ╚██╔╝  ██╔═══╝  ██║
███████║╚██╗╚██████╗   ██║   ██║     ██╔╝
╚═▀▀▀══╝ ╚═╝ ╚═════╝   ╚═╝   ╚═╝     ╚═╝ v{Util.GetAssemblyVersion()}");

            Console.WriteLine();
            Console.ResetColor();
            Log.Information("Starting Cypher...");
            Log.Information("Process ID:{@Message}", Environment.ProcessId);
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
                IPAddress ipAddress;
                var endPoint = Util.TryParseAddress(configurationRoot["Node:HttpEndPoint"][7..]);
                switch (endPoint.Address.ToString())
                {
                    case "127.0.0.1":
                        ipAddress = IPAddress.Loopback;
                        break;
                    case "0.0.0.0":
                        ipAddress = IPAddress.Any;
                        break;
                    default:
                        if (IPAddress.TryParse(endPoint.Address.ToString(), out ipAddress)) break;
                        Log.Fatal("The specified IP Address is invalid");
                        throw new Exception("[Invalid IP Address]");
                }

                options.Listen(ipAddress, endPoint.Port);
                options.Listen(ipAddress, Convert.ToInt32(configurationRoot["Node:HttpsPort"]), listenOptions =>
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