// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace CYPNode.StartupExtensions
{
    public static class SwaggerExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddSwaggerGenOptions(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    License = new Microsoft.OpenApi.Models.OpenApiLicense
                    {
                        Name = "Attribution-NonCommercial-NoDerivatives 4.0 International",
                        Url = new Uri("https://raw.githubusercontent.com/tangramproject/Tangram.Cypher/initial/LICENSE")
                    },
                    Title = "CYPNode API",
                    Version = CYPCore.Helper.Util.GetAssemblyVersion(),
                    Description = "Node.",
                    TermsOfService = new Uri("https://tangrams.io/legal/"),
                    Contact = new Microsoft.OpenApi.Models.OpenApiContact
                    {
                        Url = new Uri("https://tangrams.io/about-tangram/team/")
                    }
                });
            });

            return services;
        }
    }
}
