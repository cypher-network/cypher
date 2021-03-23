using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using rxcypcore.Consensus;
using rxcypcore.Extensions;
using rxcypnode.Monitor;
using rxcypnode.StartupExtensions;

namespace rxcypnode
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private IConfiguration Configuration { get; }
        private ILifetimeScope AutofacContainer { get; set; }
        
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression();
            services.AddMvc(option => option.EnableEndpointRouting = false).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddControllers();
            services.AddSwaggerGenOptions();
            services.AddHttpContextAccessor();
            services.AddOptions();
            services.Configure<PbftOptions>(Configuration);
            services.AddDataKeysProtection(Configuration);

            // Enable web monitor client
            services.AddRazorPages();
            services.AddSignalR();

            services.AddServerSideBlazor();
            services.AddControllersWithViews();
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddSerilog();
            
            // Serf client
            builder.AddSwimGossipClient(Configuration);
            
            // Main node object
            //builder.AddLocalNode();

            builder.AddNodeService();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostEnvironment env, IHostApplicationLifetime lifetime)
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
                endpoints.MapRazorPages();
                endpoints.MapBlazorHub();
            });
            app.UseSwagger()
                .UseSwaggerUI(option =>
                {
                    option.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "CYPNode V1");
                    option.OAuthClientId("cypherswaggerui");
                    option.OAuthAppName("CYPNode Swagger UI");
                });

            AutofacContainer = app.ApplicationServices.GetAutofacRoot();
        }
    }
}