// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;

using Autofac.Extensions.DependencyInjection;
using Autofac;

using CYPNode.StartupExtensions;
using CYPCore.Consensus;
using CYPCore.Extensions;
using CYPCore.Network;
using Polly;

namespace CYPNode
{
    public class Startup
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="env"></param>
        /// <param name="configuration"></param>
        public Startup(IWebHostEnvironment env, IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; private set; }
        public ILifetimeScope AutofacContainer { get; private set; }

        /// <summary>
        ///
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient<NetworkClient>().AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(5));
            services.AddResponseCompression();
            services.AddMvc(option => option.EnableEndpointRouting = false).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddControllers();
            services.AddSwaggerGenOptions();
            services.AddHttpContextAccessor();
            services.AddOptions();
            services.Configure<PbftOptions>(Configuration);
            services.AddDataKeysProtection(Configuration);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddSerilog();
            builder.AddNodeMonitorService(Configuration);
            builder.AddSwimGossipClient(Configuration);
            builder.AddSerfProcessService(Configuration);
            builder.AddUnitOfWork(Configuration);
            builder.AddGraph();
            builder.AddMemoryPool();
            builder.AddSigning();
            builder.AddValidator();
            builder.AddMembershipService();
            builder.AddPosMinting(Configuration);
            builder.AddSync(Configuration);
            builder.AddLocalNode();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="app"></param>
        /// <param name="lifetime"></param>
        public void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
        {
            var pathBase = Configuration["PATH_BASE"];
            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseCors("default");
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
            });

            lifetime.ApplicationStopping.Register(() =>
            {
            });
        }
    }
}
