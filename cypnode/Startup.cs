// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Autofac;
using CYPNode.StartupExtensions;
using CYPCore.Consensus;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Serilog;

namespace CYPNode
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

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
            services.AddQueuePolicy(options =>
            {
                options.MaxConcurrentRequests = 750;
                options.RequestQueueLimit = 15000;
            });
            services.AddHttpClient<NetworkClient>().SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddPolicyHandler(GetRetryPolicy()).AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(5));
            services.AddResponseCompression();
            services.AddMvc(option => option.EnableEndpointRouting = false)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddControllers();
            services.AddSwaggerGenOptions();
            services.AddHttpContextAccessor();
            services.AddOptions()
                .Configure<NetworkSetting>(options => _configuration.GetSection("Network").Bind(options));
            services.Configure<PbftOptions>(_configuration);
            services.AddDataKeysProtection(_configuration);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions.HandleTransientHttpError()
                .Or<TimeoutRejectedException>().WaitAndRetryAsync(3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan, context) =>
                    {
                        Log.Error(exception.Exception.Message);
                    });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddSerilog();
            builder.AddNodeMonitorService(_configuration);
            builder.AddSwimGossipClient(_configuration);
            builder.AddSerfProcessService(_configuration);
            builder.AddUnitOfWork(_configuration);
            builder.AddGraph();
            builder.AddMemoryPool();
            builder.AddSigning();
            builder.AddValidator();
            builder.AddMembershipService();
            builder.AddPosMinting(_configuration);
            builder.AddLocalNode();
            builder.AddSync(_configuration);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="app"></param>
        /// <param name="lifetime"></param>
        public void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
        {
            var pathBase = _configuration["PATH_BASE"];
            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }

            app.UseConcurrencyLimiter();
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
        }
    }
}
