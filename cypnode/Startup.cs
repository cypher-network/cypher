// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using CYPNode.StartupExtensions;
using CYPCore.Consensus;
using CYPCore.Extensions;
using CYPCore.Ledger;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Services;
using CYPCore.GossipMesh;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace CYPNode
{
    public class Startup
    {
        private readonly IConfiguration _configuration;
        public ILifetimeScope AutofacContainer { get; private set; }

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
            services.AddProtoActorSystem();
            services.AddResponseCompression();
            services.AddMvc(option => option.EnableEndpointRouting = false)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddSwaggerGenOptions();
            services.AddHttpContextAccessor();
            services.AddOptions()
                .Configure<AppOptions>(options => _configuration.GetSection("Node").Bind(options));
            services.Configure<PbftOptions>(_configuration);
            services.AddDataKeysProtection(_configuration);
            services.AddSingleton<IGossipMemberStore, GossipMemberStore>();
            services.AddSingleton<IGossipMemberEventsStore, GossipMemberEventsStore>();
            services.AddSingleton<IMemberListener, MemberListener>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddSerilog();
            builder.AddUnitOfWork(_configuration);
            builder.AddGraph();
            builder.AddMemoryPool();
            builder.AddValidator();
            builder.AddWallet();
            builder.AddPosMinting(_configuration);
            builder.AddSync(_configuration);
            builder.AddGossip(_configuration);
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
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseSwagger()
               .UseSwaggerUI(c =>
               {
                   c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "CYPNode V1");
                   c.OAuthClientId("cypherswaggerui");
                   c.OAuthAppName("CYPNode Swagger UI");
               });


            AutofacContainer = app.ApplicationServices.GetAutofacRoot();

            lifetime.ApplicationStarted.Register(() =>
            {
                AutofacContainer.Resolve<IGossipServer>();
                AutofacContainer.Resolve<IPosMinting>();
            });

            lifetime.ApplicationStopping.Register(() =>
            {

            });
        }
    }
}
