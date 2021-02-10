// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

using Autofac;

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
        public static ContainerBuilder AddPosMinting(this ContainerBuilder builder, IConfigurationRoot configurationRoot)
        {
            builder.Register(c =>
            {
                var stakingConfigurationOptions = new StakingConfigurationOptions();
                configurationRoot.Bind("Staking", stakingConfigurationOptions);

                var posMintingProvider = new PosMinting(
                    c.Resolve<ISerfClient>(),
                    c.Resolve<IUnitOfWork>(),
                    c.Resolve<ISigning>(),
                    c.Resolve<IValidator>(),
                    c.Resolve<ILocalNode>(),
                    stakingConfigurationOptions,
                    c.Resolve<ILogger<PosMinting>>());

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
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static ContainerBuilder AddStoredbContext(this ContainerBuilder builder, IConfigurationRoot configurationRoot)
        {
            var dataFolder = configurationRoot.GetSection("DataFolder");

            builder.Register(c =>
            {
                var lifetime = c.Resolve<IHostApplicationLifetime>();
                var storedbContext = new StoredbContext(lifetime, dataFolder.Value);
                return storedbContext;
            })
            .As<IStoredbContext>()
            .SingleInstance();

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddUnitOfWork(this ContainerBuilder builder)
        {
            builder.RegisterType<UnitOfWork>().As<IUnitOfWork>().SingleInstance();
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
                     c.Resolve<ILogger<LocalNode>>()
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
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSwimGossipClient(this ContainerBuilder builder, IConfigurationRoot configurationRoot)
        {
            builder.Register(c =>
            {
                var serfConfigurationOptions = new SerfConfigurationOptions();
                var apiConfigurationOptions = new ApiConfigurationOptions();

                configurationRoot.Bind("Serf", serfConfigurationOptions);
                configurationRoot.Bind("Api", apiConfigurationOptions);

                var logger = c.Resolve<ILogger<SerfClient>>();
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
        public static ContainerBuilder AddValidator(this ContainerBuilder builder, IConfigurationRoot configurationRoot)
        {
            builder.Register(c =>
            {
                Validator validator = new Validator(c.Resolve<IUnitOfWork>(), c.Resolve<ISigning>(), c.Resolve<ILogger<Validator>>());
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
        public static IServiceCollection AddDataKeysProtection(this IServiceCollection services, IConfigurationRoot configurationRoot)
        {
            services.AddSingleton<IDataProtectionKeyRepository, DataProtectionKeyRepository>(sp =>
            {
                var dataProtectionKeyRepository = new DataProtectionKeyRepository(sp.GetService<IStoredbContext>());
                return dataProtectionKeyRepository;
            });

            var dataProtecttion = configurationRoot.GetSection("DataProtectionPath");

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
        /// <returns></returns>
        public static ContainerBuilder AddDataKeysProtection(this ContainerBuilder builder)
        {
            builder.Register(c =>
            {
                var dataProtectionKeyRepository = new DataProtectionKeyRepository(c.Resolve<IStoredbContext>());
                return dataProtectionKeyRepository;
            })
            .As<IDataProtectionKeyRepository>();

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configurationRoot"></param>
        /// <returns></returns>
        public static ContainerBuilder AddSerfProcessService(this ContainerBuilder builder, IConfigurationRoot configurationRoot)
        {
            builder.Register(c =>
            {
                var ct = new CancellationTokenSource();
                var localNode = c.Resolve<ILocalNode>();
                var signing = c.Resolve<ISigning>();
                var lifetime = c.Resolve<IHostApplicationLifetime>();
                var serfClient = c.Resolve<ISerfClient>();
                var logger = c.Resolve<ILogger<SerfService>>();

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
                    var seedNodesSection = configurationRoot.GetSection("SeedNodes").GetChildren().ToList();
                    if (seedNodesSection.Any())
                    {
                        var seedNodes = new SeedNode(seedNodesSection.Select(x => x.Value));
                        serfService.JoinSeedNodes(seedNodes).ConfigureAwait(false).GetAwaiter();
                    }

                    localNode.Ready();
                }
                else
                {
                    logger.LogCritical($"<<< GraphProvider.InitializeBlocks >>>: {((SerfError)connectResult.NonSuccessMessage).Error}");
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
