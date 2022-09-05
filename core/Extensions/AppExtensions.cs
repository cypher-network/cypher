// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Autofac;
using AutofacSerilogIntegration;
using CypherNetwork.Cryptography;
using CypherNetwork.Helper;
using CypherNetwork.Ledger;
using CypherNetwork.Models;
using CypherNetwork.Network;
using CypherNetwork.Persistence;
using CypherNetwork.Services;
using CypherNetwork.Wallet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CypherNetwork.Extensions;

public static class AppExtensions
{
    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static void AddSerilog(this ContainerBuilder builder)
    {
        builder.RegisterLogger();
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static ContainerBuilder AddCypherNetworkCore(this ContainerBuilder builder, IConfiguration configuration)
    {
        builder.Register(c =>
        {
            var options = new AppOptions
            {
                Data = new DataOptions
                {
                    RocksDb = configuration["Node:Data:RocksDb"],
                    KeysProtectionPath = configuration["Node:Data:KeysProtectionPath"]
                },
                Gossip = new GossipOptions
                {
                    Advertise = configuration["Node:Gossip:Advertise"].ToBytes(),
                    Listening = configuration["Node:Gossip:Listening"].ToBytes()
                }
            };
            var nodes = configuration.GetSection("Node:Gossip:Seeds").GetChildren().ToArray();
            options.Gossip.Seeds = nodes.Select(n =>
                new Node { Advertise = n["Advertise"].ToBytes(), Listening = n["Listening"].ToBytes() }).ToArray();
            options.Name = configuration["Node:Name"];
            options.Network = new NetworkSetting
            {
                Environment = configuration["Node:Network:Environment"],
                X509Certificate =
                    new CypherNetwork.Models.X509Certificate
                    {
                        Password = configuration["Node:Network:X509Certificate:Password"],
                        Thumbprint = configuration["Node:Network:X509Certificate:Thumbprint"],
                        CertPath = configuration["Node:Network:X509Certificate:CertPath"]
                    },
                TransactionRateConfig = new TransactionLeakRateConfigurationOption
                {
                    LeakRate = Convert.ToInt32(configuration["Node:Network:TransactionRateConfig:LeakRate"]),
                    MaxFill = Convert.ToInt32(configuration["Node:Network:TransactionRateConfig:MaxFill"]),
                    LeakRateNumberOfSeconds =
                        Convert.ToInt32(
                            configuration["Node:Network:TransactionRateConfig:LeakRateNumberOfSeconds"])
                },
                SigningKeyRingName = configuration["Node:Network:SigningKeyRingName"],
                AutoSyncEveryMinutes = Convert.ToInt16(configuration["Node:Network:AutoSyncEveryMinutes"])
            };
            options.Staking = new StakingOptions
            {
                TransactionsPerBlock = Convert.ToInt32(configuration["Node:Staking:TransactionsPerBlock"])
            };
            options.HttpsPort = Convert.ToInt32(configuration["Node:HttpsPort"]);
            options.HttpEndPoint = configuration["Node:HttpEndPoint"];
            var cypherNetworkCore = new CypherNetworkCore(c.Resolve<IHostApplicationLifetime>(),
                c.Resolve<IServiceScopeFactory>(), options, c.Resolve<ILogger>());
            return cypherNetworkCore;
        }).As<ICypherNetworkCore>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddPeerDiscovery(this ContainerBuilder builder)
    {
        builder.RegisterType<PeerDiscovery>().As<IPeerDiscovery>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddP2PDevice(this ContainerBuilder builder)
    {
        builder.RegisterType<P2PDeviceApi>().As<IP2PDeviceApi>().InstancePerDependency();
        builder.RegisterType<P2PDevice>().As<IP2PDevice>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddBroadcast(this ContainerBuilder builder)
    {
        builder.RegisterType<Broadcast>().As<IBroadcast>().InstancePerDependency();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    public static ContainerBuilder AddLongRunningService(this ContainerBuilder builder)
    {
        builder.RegisterType<LongRunningService>().As<IHostedService>().InstancePerDependency();
        builder.RegisterType<BackgroundWorkerQueue>().As<IBackgroundWorkerQueue>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddMemoryPool(this ContainerBuilder builder)
    {
        builder.RegisterType<MemoryPool>().As<IMemoryPool>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddPPoS(this ContainerBuilder builder)
    {
        builder.RegisterType<PPoS>().As<IPPoS>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static ContainerBuilder AddUnitOfWork(this ContainerBuilder builder, IConfiguration configuration)
    {
        builder.Register(c =>
        {
            UnitOfWork unitOfWork = new(configuration["Node:Data:RocksDb"], c.Resolve<ILogger>());
            return unitOfWork;
        }).As<IUnitOfWork>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddGraph(this ContainerBuilder builder)
    {
        builder.RegisterType<Graph>().As<IGraph>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddValidator(this ContainerBuilder builder)
    {
        builder.RegisterType<Validator>().As<IValidator>().InstancePerDependency();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddCrypto(this ContainerBuilder builder)
    {
        builder.RegisterType<Crypto>().As<ICrypto>().InstancePerDependency();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddDataKeysProtection(this IServiceCollection services,
        IConfiguration configuration)
    {
        X509Certificate2 certificate;
        if (!string.IsNullOrEmpty(configuration["Node:Network:X509Certificate:CertPath"]) &&
            !string.IsNullOrEmpty(configuration["Node:Network:X509Certificate:Password"]))
            certificate = new X509Certificate2(configuration["Node:Network:X509Certificate:CertPath"],
                configuration["Node:Network:X509Certificate:Password"]);
        else
            certificate =
                new CertificateResolver().ResolveCertificate(configuration["Node:Network:X509Certificate:Thumbprint"]);

        if (certificate != null)
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Util.EntryAssemblyPath(),
                    configuration["Node:Data:KeysProtectionPath"]))).ProtectKeysWithCertificate(certificate)
                .SetApplicationName(configuration["Node:Name"]).SetDefaultKeyLifetime(TimeSpan.FromDays(3650));
        return services;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddSync(this ContainerBuilder builder)
    {
        builder.RegisterType<Sync>().As<ISync>().SingleInstance();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static ContainerBuilder AddNodeMonitorService(this ContainerBuilder builder,
        IConfiguration configuration)
    {
        builder.Register(c =>
        {
            var nodeMonitorConfigurationOptions = new NodeMonitorConfigurationOptions();
            configuration.Bind(NodeMonitorConfigurationOptions.ConfigurationSectionName,
                nodeMonitorConfigurationOptions);
            var nodeMonitorProvider =
                new NodeMonitor(nodeMonitorConfigurationOptions, c.Resolve<ILogger>());
            return nodeMonitorProvider;
        }).As<INodeMonitor>().InstancePerLifetimeScope();
        builder.RegisterType<NodeMonitorService>().As<IHostedService>();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static ContainerBuilder AddNodeWallet(this ContainerBuilder builder)
    {
        builder.RegisterType<NodeWallet>().As<INodeWallet>().InstancePerDependency();
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <param name="builder"></param>
    public static ContainerBuilder AddNodeWalletSession(this ContainerBuilder builder)
    {
        builder.RegisterType<WalletSession>().As<IWalletSession>().SingleInstance();
        return builder;
    }
}