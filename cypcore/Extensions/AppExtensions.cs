// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Autofac;
using AutofacSerilogIntegration;
using CYPCore.Models;
using CYPCore.Services;
using CYPCore.Persistence;
using CYPCore.Network;
using CYPCore.Cryptography;
using CYPCore.Helper;
using CYPCore.Ledger;
using CYPCore.Network.Commands;
using CYPCore.Wallet;
using Microsoft.Extensions.Options;
using Proto;
using Proto.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;
using IMemberListener = CYPCore.GossipMesh.IMemberListener;

namespace CYPCore.Extensions
{
    public static class AppExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSerilog(this ContainerBuilder builder)
        {
            builder.RegisterLogger();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddMemoryPool(this ContainerBuilder builder)
        {
            builder.RegisterType<MemoryPool>().As<IMemoryPool>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddPosMinting(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var posMintingProvider = new PosMinting(c.Resolve<ActorSystem>(), c.Resolve<IMemoryPool>(),
                    c.Resolve<INodeWallet>(), c.Resolve<IValidator>(), c.Resolve<ISync>(),
                    c.Resolve<IOptions<AppOptions>>(), c.Resolve<Serilog.ILogger>());
                return posMintingProvider;
            }).As<IPosMinting>().SingleInstance();
            return builder;
        }

        /// <summary>
        ///     
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddUnitOfWork(this ContainerBuilder builder, IConfiguration configuration)
        {
            var appConfigurationOptions = new AppOptions();
            configuration.Bind("Node", appConfigurationOptions);
            builder.Register(c =>
            {
                UnitOfWork unitOfWork = new(appConfigurationOptions.Data.RocksDb, c.Resolve<ILogger>());
                return unitOfWork;
            }).As<IUnitOfWork>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddGraph(this ContainerBuilder builder)
        {
            builder.RegisterType<Graph>().As<IGraph>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddGossip(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var appConfigurationOptions = new AppOptions();
                configuration.Bind("Node", appConfigurationOptions);
                var logger = new SerilogLoggerProvider(c.Resolve<ILogger>()).CreateLogger(nameof(IGossipServer));
                var seedNodes = appConfigurationOptions.Gossip.SeedNodes != null
                    ? new IPEndPoint[appConfigurationOptions.Gossip.SeedNodes.Count]
                    : Array.Empty<IPEndPoint>();
                if (appConfigurationOptions.Gossip.SeedNodes != null)
                    seedNodes = appConfigurationOptions.Gossip.SeedNodes.Select(endPoint => endPoint.Split(":"))
                        .Select(endPointValues =>
                            new IPEndPoint(IPAddress.Parse(endPointValues[0]), ushort.Parse(endPointValues[1])))
                        .ToArray();
                var gossipService = new GossipServer(IPEndPoint.Parse(appConfigurationOptions.Gossip.Listening),
                    seedNodes, c.Resolve<IMemberListener>(), c.Resolve<IHostApplicationLifetime>(), logger);
                gossipService.StartAsync().ConfigureAwait(false);
                return gossipService;
            }).As<IGossipServer>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddValidator(this ContainerBuilder builder)
        {
            builder.Register(c =>
            {
                var validator = new Validator(c.Resolve<ActorSystem>(), c.Resolve<IUnitOfWork>(),
                    c.Resolve<ILogger>());
                return validator;
            }).As<IValidator>().InstancePerDependency();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static IServiceCollection AddDataKeysProtection(this IServiceCollection services,
            IConfiguration configuration)
        {
            AppOptions appConfigurationOptions = new();
            configuration.Bind("Node", appConfigurationOptions);
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(appConfigurationOptions.Data.KeysProtectionPath))
                .SetApplicationName("cypher").SetDefaultKeyLifetime(TimeSpan.FromDays(3650));
            return services;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddProtoActorSystem(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var config = ActorSystemConfig.Setup().WithDeadLetterThrottleCount(3)
                    .WithDeadLetterThrottleInterval(TimeSpan.FromSeconds(1)).WithDeveloperSupervisionLogging(false);
                var system = new ActorSystem(config);
                system.WithServiceProvider(provider);
                return system;
            });
            
            services.AddTransient<ShimCommands>();
            services.AddTransient<LocalNode>();
            services.AddTransient<CryptoKeySign>();
            return services;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSync(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var sync = new Sync(c.Resolve<ActorSystem>(), c.Resolve<IValidator>(),
                    c.Resolve<IHostApplicationLifetime>(), c.Resolve<ILogger>());
                return sync;
            }).As<ISync>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
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
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddWallet(this ContainerBuilder builder)
        {
            builder.RegisterType<NodeWallet>().As<INodeWallet>().SingleInstance();
            return builder;
        }
    }
}