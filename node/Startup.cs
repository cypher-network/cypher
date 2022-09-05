// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using CypherNetwork;
using CypherNetworkNode.StartupExtensions;
using CypherNetwork.Consensus;
using CypherNetwork.Cryptography;
using CypherNetwork.Extensions;
using CypherNetwork.Ledger;
using CypherNetwork.Network;
using CypherNetwork.Services;
using CypherNetwork.Wallet;
using Serilog;
using Spectre.Console;
using Log = Serilog.Log;

namespace CypherNetworkNode;

public class Startup
{
    public static bool ShowPrivateKey = false;
    private readonly IConfiguration _configuration;
    private ILifetimeScope AutofacContainer { get; set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="env"></param>
    /// <param name="configuration"></param>
    public Startup(IWebHostEnvironment env, IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="services"></param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddResponseCompression();
        services.AddMvc(option => option.EnableEndpointRouting = false);
        services.AddSwaggerGenOptions();
        services.AddHttpContextAccessor();
        services.Configure<BlockmaniaOptions>(_configuration);
        services.AddDataKeysProtection(_configuration);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="builder"></param>
    public void ConfigureContainer(ContainerBuilder builder)
    {
        builder.AddSerilog();
        builder.AddCypherNetworkCore(_configuration);
        builder.AddCrypto();
        builder.AddLongRunningService();
        builder.AddValidator();
        builder.AddNodeWalletSession();
        builder.AddNodeWallet();
        builder.AddPeerDiscovery();
        builder.AddBroadcast();
        builder.AddP2PDevice();
        builder.AddUnitOfWork(_configuration);
        builder.AddGraph();
        builder.AddMemoryPool();
        builder.AddPPoS();
        builder.AddSync();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="app"></param>
    /// <param name="lifetime"></param>
    public void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
    {
        ServiceActivator.Configure(app.ApplicationServices);
        var pathBase = _configuration["PATH_BASE"];
        if (!string.IsNullOrEmpty(pathBase))
        {
            app.UsePathBase(pathBase);
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        app.UseSwagger().UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint(
                $"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json",
                "Cypher Network V1");
            c.OAuthClientId("cypherswaggerui");
            c.OAuthAppName("Cypher Network Swagger UI");
        });
        AutofacContainer = app.ApplicationServices.GetAutofacRoot();
        lifetime.ApplicationStarted.Register(() =>
        {
            const string logSectionName = "Log";
            if (((ConfigurationRoot)_configuration).GetSection(logSectionName) != null)
            {
                Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(_configuration, logSectionName)
                    .CreateLogger();
            }

            if (ShowPrivateKey)
            {
                var cypherSystem = AutofacContainer.Resolve<ICypherNetworkCore>();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("The node private key can be used to decrypt and confirm that the command/message came from the stated sender (i.e. your wallet/yourself).\n" +
                                  "In order to stake, you will need to access your Bamboo wallet to configure the node setup process. In order to do so please open the Bamboo wallet\n" +
                                  "and use the stake command (type @ \"stake\" in your wallet control window), following which prompts will appear.\n" +
                                  "Please write down your node private key and store it somewhere safe and secure (note you can generate your node private key at any time via\n" +
                                  "using the showkey command (type \"--configure --showkey\" in your node control window).\n" +
                                  "Further, a second security feature exists in the form of a token which can be used as an additional key to authenticate commands/messages.\n" +
                                  "Your token can be randomly generated by using the command \"--configure --showkey\", which also provides your node private key as detailed above.\n" +
                                  "The token is randomly generated every time and you do not need to store it somewhere safe.\n" +
                                  "However, you do need the token when you encrypt and decrypt commands/messages received by your node.");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("-----------------------------------------------------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"| Cypher private key: {cypherSystem.KeyPair.PrivateKey.FromSecureString()} |");
                Console.WriteLine($"| Cypher token:       {Crypto.GetRandomData().ByteToHex()}                                 |");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("-----------------------------------------------------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine();
                Console.WriteLine("Shutting down Cypher...");
                Environment.Exit(1);
                return;
            }

            AnsiConsole.Status()
                .Start("Starting...", ctx => 
                {
                    AnsiConsole.MarkupLine("Begin...     [bold green]PEER DISCOVERY[/]");
                    AutofacContainer.Resolve<IPeerDiscovery>();

                    AnsiConsole.MarkupLine("Begin...     [bold green]P2P DEVICE[/]");
                    AutofacContainer.Resolve<IP2PDevice>();

                    AnsiConsole.MarkupLine("Begin...     [bold green]WALLET SESSION[/]");
                    AutofacContainer.Resolve<IWalletSession>();

                    AnsiConsole.MarkupLine("Begin...     [bold green]PURE PROOF OF STAKE[/]");
                    AutofacContainer.Resolve<IPPoS>();

                    AnsiConsole.MarkupLine("Begin...     [bold green]SYNC[/]");
                    AutofacContainer.Resolve<ISync>();
                });

        });
        lifetime.ApplicationStopping.Register(() => { });
    }
}