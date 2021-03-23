using System;
using System.IO;
using Autofac;
using AutofacSerilogIntegration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using rxcypcore.Models;
using rxcypcore.Network;
using rxcypcore.Serf;
using rxcypcore.Services;
using Serilog;

namespace rxcypcore.Extensions
{
    public static class AppExtensions
    {
        public static IServiceCollection AddDataKeysProtection(this IServiceCollection services, IConfiguration configuration)
        {
            var dataProtection = configuration.GetSection("DataProtectionPath");

            services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtection.Value))
                .SetApplicationName("cypher")
                .SetDefaultKeyLifetime(TimeSpan.FromDays(3650));

            return services;
        }

        public static ContainerBuilder AddLocalNode(this ContainerBuilder builder)
        {
            builder.RegisterType<LocalNode>().As<ILocalNode>();
            return builder;
        }

        public static ContainerBuilder AddNodeService(this ContainerBuilder builder)
        {
            builder.RegisterType<NodeService>().As<IHostedService>();
            return builder;
        }

        public static ContainerBuilder AddSerilog(this ContainerBuilder builder)
        {
            builder.RegisterLogger();
            return builder;
        }

        public static ContainerBuilder AddSwimGossipClient(this ContainerBuilder builder, IConfiguration configuration)
        {
            builder.Register(context =>
                {
                    var serfConfigurationOptions = new SerfConfigurationOptions();
                    configuration.Bind("Serf", serfConfigurationOptions);

                    var logger = context.Resolve<ILogger>();
                    var serfClient = new SerfClient(serfConfigurationOptions, logger);
                    return serfClient;
                })
                .As<ISerfClient>()
                .SingleInstance();

            return builder;
        }
    }
}