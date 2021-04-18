// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Autofac;
using AutofacSerilogIntegration;
using CYPCore.Models;
using CYPCore.Services;
using CYPCore.Serf;
using CYPCore.Persistence;
using CYPCore.Network;
using CYPCore.Cryptography;
using CYPCore.Helper;
using CYPCore.Ledger;

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
        public static ContainerBuilder AddSigning(this ContainerBuilder builder)
        {
            builder.RegisterType<Signing>().As<ISigning>().InstancePerLifetimeScope();
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
                var stakingConfigurationOptions = new StakingConfigurationOptions();
                configuration.Bind("Staking", stakingConfigurationOptions);
                var posMintingProvider = new PosMinting(c.Resolve<IGraph>(), c.Resolve<IMemoryPool>(),
                    c.Resolve<ISerfClient>(), c.Resolve<IUnitOfWork>(), c.Resolve<ISigning>(), c.Resolve<IValidator>(),
                    c.Resolve<ISync>(), stakingConfigurationOptions, c.Resolve<Serilog.ILogger>(), c.Resolve<IHostApplicationLifetime>());
                return posMintingProvider;
            }).As<IStartable>().SingleInstance();
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
            var dataFolder = configuration.GetSection("DataFolder");
            builder.Register(c =>
            {
                UnitOfWork unitOfWork = new(dataFolder.Value, c.Resolve<Serilog.ILogger>());
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
        /// <returns></returns>
        public static ContainerBuilder AddLocalNode(this ContainerBuilder builder)
        {
            builder.RegisterType<LocalNode>().As<ILocalNode>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSwimGossipClient(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var serfConfigurationOptions = new SerfConfigurationOptions();
                var apiConfigurationOptions = new ApiConfigurationOptions();
                var serfSeedNodes = new SerfSeedNodes();
                configuration.Bind("SeedNodes", serfSeedNodes);
                configuration.Bind("Serf", serfConfigurationOptions);
                configuration.Bind("Api", apiConfigurationOptions);
                var logger = c.Resolve<Serilog.ILogger>();
                var serfClient = new SerfClient(c.Resolve<ISigning>(), serfConfigurationOptions,
                    apiConfigurationOptions, serfSeedNodes, logger);
                return serfClient;
            }).As<ISerfClient>().SingleInstance();
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
                Validator validator = new Validator(c.Resolve<IUnitOfWork>(), c.Resolve<ISigning>(),
                    c.Resolve<Serilog.ILogger>());
                return validator;
            }).As<IValidator>().SingleInstance();
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
            var dataProtection = configuration.GetSection("DataProtectionPath");
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtection.Value))
                .SetApplicationName("cypher").SetDefaultKeyLifetime(TimeSpan.FromDays(3650));
            return services;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSerfProcessService(this ContainerBuilder builder,
            IConfiguration configuration)
        {
            // Do not start Serf when NodeMonitor is active
            var nodeMonitorConfigurationOptions = new NodeMonitorConfigurationOptions();
            configuration.Bind(NodeMonitorConfigurationOptions.ConfigurationSectionName,
                nodeMonitorConfigurationOptions);
            builder.Register(c =>
            {
                ISerfService serfService = null;
                var logger = c.Resolve<Serilog.ILogger>();
                try
                {
                    var ct = new CancellationTokenSource();
                    var localNode = c.Resolve<ILocalNode>();
                    var signing = c.Resolve<ISigning>();
                    var lifetime = c.Resolve<IHostApplicationLifetime>();
                    var serfClient = c.Resolve<ISerfClient>();
                    serfService = nodeMonitorConfigurationOptions.Enabled
                        ? new SerfServiceTester(serfClient, signing, logger)
                        : new SerfService(serfClient, signing, logger);
                    serfService.StartAsync(lifetime).GetAwaiter();
                    if (serfService.Disabled) return serfService;
                    ct.CancelAfter(100000);
                    while (!ct.IsCancellationRequested && !serfClient.ProcessStarted)
                    {
                        Task.Delay(100, ct.Token);
                    }

                    var tcpSession = serfClient.TcpSessionsAddOrUpdate(
                        new TcpSession(serfClient.SerfConfigurationOptions.Listening).Connect(serfClient
                            .SerfConfigurationOptions.RPC));
                    var connectResult = serfClient.Connect(tcpSession.SessionId).GetAwaiter().GetResult();
                    if (connectResult.Success)
                    {
                        var seedNodesSection = configuration.GetSection("SeedNodes").GetChildren().ToList();
                        if (!seedNodesSection.Any()) return serfService;
                        var seedNodes = new SeedNode(seedNodesSection.Select(x => x.Value));
                        serfService.JoinSeedNodes(seedNodes).GetAwaiter();
                    }
                    else
                    {
                        logger.Here().Error("{@Error}", ((SerfError)connectResult.NonSuccessMessage).Error);
                    }

                    localNode.Ready();
                }
                catch (TaskCanceledException ex)
                {
                    logger.Here().Error(ex, "Starting serf timeout error");
                }
                catch (OperationCanceledException ex)
                {
                    logger.Here().Error(ex, "Starting serf operation canceled error");
                }
                catch (Exception ex)
                {
                    logger.Here().Error(ex, "Starting serf error");
                }

                return serfService;
            }).As<IStartable>().SingleInstance();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSync(this ContainerBuilder builder)
        {
            builder.RegisterType<Sync>().As<ISync>().SingleInstance();
            builder.RegisterType<SyncBackgroundService>().As<IHostedService>();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddMembershipService(this ContainerBuilder builder)
        {
            builder.RegisterType<MembershipService>().As<IMembershipService>();
            return builder;
        }

        public static ContainerBuilder AddNodeMonitorService(this ContainerBuilder builder,
            IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var nodeMonitorConfigurationOptions = new NodeMonitorConfigurationOptions();
                configuration.Bind(NodeMonitorConfigurationOptions.ConfigurationSectionName,
                    nodeMonitorConfigurationOptions);
                var nodeMonitorProvider =
                    new NodeMonitor(nodeMonitorConfigurationOptions, c.Resolve<Serilog.ILogger>());
                return nodeMonitorProvider;
            }).As<INodeMonitor>().InstancePerLifetimeScope();
            builder.RegisterType<NodeMonitorService>().As<IHostedService>();
            return builder;
        }
    }
}