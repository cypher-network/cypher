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
using CYPCore.Ledger;

namespace CYPCore.Extensions
{
    public static class AppExtenstions
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
        public static ContainerBuilder AddMempool(this ContainerBuilder builder)
        {
            builder.RegisterType<MemoryPool>().As<IMemoryPool>().InstancePerDependency();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddPosMinting(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var stakingConfigurationOptions = new StakingConfigurationOptions();
                configuration.Bind("Staking", stakingConfigurationOptions);

                var posMintingProvider = new PosMinting(
                    c.Resolve<ISerfClient>(),
                    c.Resolve<IUnitOfWork>(),
                    c.Resolve<ISigning>(),
                    c.Resolve<IValidator>(),
                    c.Resolve<ILocalNode>(),
                    stakingConfigurationOptions,
                    c.Resolve<Serilog.ILogger>());

                return posMintingProvider;
            })
            .As<IPosMinting>()
            .InstancePerLifetimeScope();


            builder.RegisterType<PosMintingBackgroundService>().As<IHostedService>().SingleInstance();

            return builder;
        }

        /// <summary>
        ///     
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddUnitOfWork(this ContainerBuilder builder, IConfiguration configuration)
        {
            var dataFolder = configuration.GetSection("DataFolder");
            builder.Register(c =>
            {
                UnitOfWork unitOfWork = new(dataFolder.Value, c.Resolve<Serilog.ILogger>());
                return unitOfWork;
            })
            .As<IUnitOfWork>()
            .SingleInstance();

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddStaging(this ContainerBuilder builder)
        {
            builder.RegisterType<Staging>().As<IStaging>().InstancePerLifetimeScope();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddLocalNode(this ContainerBuilder builder)
        {
            builder.Register(c =>
            {
                var localNode = new LocalNode
                (
                     c.Resolve<ISerfClient>(),
                     c.Resolve<Serilog.ILogger>()
                 );

                return localNode;
            })
            .As<ILocalNode>()
            .SingleInstance();

            builder.RegisterType<LocalNodeBackgroundService>().As<IHostedService>().SingleInstance();
            builder.RegisterType<SyncBackgroundService>().As<IHostedService>().SingleInstance();

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

                configuration.Bind("Serf", serfConfigurationOptions);
                configuration.Bind("Api", apiConfigurationOptions);

                var logger = c.Resolve<Serilog.ILogger>();
                var serfClient = new SerfClient(c.Resolve<ISigning>(), serfConfigurationOptions, apiConfigurationOptions, logger);

                return serfClient;
            })
            .As<ISerfClient>()
            .SingleInstance();

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static ContainerBuilder AddValidator(this ContainerBuilder builder)
        {
            builder.Register(c =>
            {
                Validator validator = new Validator(c.Resolve<IUnitOfWork>(), c.Resolve<ISigning>(), c.Resolve<Serilog.ILogger>());
                return validator;
            })
            .As<IValidator>()
            .SingleInstance();

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static IServiceCollection AddDataKeysProtection(this IServiceCollection services, IConfiguration configuration)
        {
            var dataProtecttion = configuration.GetSection("DataProtectionPath");

            services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtecttion.Value))
                .SetApplicationName("tangram")
                .SetDefaultKeyLifetime(TimeSpan.FromDays(3650));

            return services;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSerfProcessService(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(c =>
            {
                var ct = new CancellationTokenSource();
                var localNode = c.Resolve<ILocalNode>();
                var signing = c.Resolve<ISigning>();
                var lifetime = c.Resolve<IHostApplicationLifetime>();
                var serfClient = c.Resolve<ISerfClient>();
                var logger = c.Resolve<Serilog.ILogger>();

                var serfService = new SerfService(serfClient, signing, logger);

                serfService.StartAsync(lifetime).ConfigureAwait(false).GetAwaiter();

                ct.CancelAfter(30000);

                while (!ct.IsCancellationRequested && !serfClient.ProcessStarted)
                {
                    Task.Delay(100, ct.Token);
                }

                var tcpSession = serfClient.TcpSessionsAddOrUpdate(new TcpSession
                    (serfClient.SerfConfigurationOptions.Listening).Connect(serfClient.SerfConfigurationOptions.RPC));

                var connectResult = serfClient.Connect(tcpSession.SessionId).ConfigureAwait(false).GetAwaiter().GetResult();

                if (connectResult.Success)
                {
                    var seedNodesSection = configuration.GetSection("SeedNodes").GetChildren().ToList();
                    if (seedNodesSection.Any())
                    {
                        var seedNodes = new SeedNode(seedNodesSection.Select(x => x.Value));
                        serfService.JoinSeedNodes(seedNodes).ConfigureAwait(false).GetAwaiter();
                    }

                    localNode.Ready();
                }
                else
                {
                    logger.Here().Error("{@Error}", ((SerfError)connectResult.NonSuccessMessage).Error);
                }

                return serfService;
            })
            .As<IStartable>()
            .SingleInstance();

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
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddMemoryPoolService(this ContainerBuilder builder)
        {
            builder.RegisterType<MemoryPoolService>().As<IMemoryPoolService>();
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddBlockService(this ContainerBuilder builder)
        {
            builder.RegisterType<BlockService>().As<IBlockService>();
            return builder;
        }
    }
}
