// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;

// TODO: Check remove. Autofac is not (yet) used in the project. Remove or start using IoC
using Autofac.Extensions.DependencyInjection;
using Autofac;

using CYPNode.StartupExtensions;
using CYPCore.Consensus.BlockMania;
using CYPCore.Extensions;

namespace CYPNode
{
    public class Startup
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="env"></param>
        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; private set; }
        public ILifetimeScope AutofacContainer { get; private set; }

        /// <summary>
        ///
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression();
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddMvc(option => option.EnableEndpointRouting = false);
            services.AddControllers();
            services.AddSwaggerGenOptions();
            services.AddHttpContextAccessor();
            services.AddOptions();
            services.Configure<PBFTOptions>(Configuration);
            services.AddDataKeysProtection(Configuration);
        }

        /// <summary>
        /// TODO: Check deletion. This method is currently unused.
        /// </summary>
        /// <param name="builder"></param>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddSwimGossipClient(Configuration);
            builder.AddSerfProcessService(Configuration);
            builder.AddStoredbContext(Configuration);
            builder.AddUnitOfWork();
            builder.AddMempool();
            builder.AddStaging();
            builder.AddSigning();
            builder.AddValidator(Configuration);
            builder.AddBlockService();
            builder.AddTransactionService();
            builder.AddMempoolService();
            builder.AddMemberService();
            builder.AddPosMinting(Configuration);
            builder.AddSync();
            builder.AddLocalNode();
            builder.AddBlockHeaderSocketService();
            builder.AddMempoolSocketService();
            builder.AddDataKeysProtection();
        }

        /// <summary>
        /// TODO: Check deletion. This method is currently unused.
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
