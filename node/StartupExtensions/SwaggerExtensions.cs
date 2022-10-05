// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace CypherNetworkNode.StartupExtensions;

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
            options.SwaggerDoc("v2", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                License = new Microsoft.OpenApi.Models.OpenApiLicense
                {
                    Name = "Attribution-NonCommercial-NoDerivatives 4.0 International",
                    Url = new Uri("https://raw.githubusercontent.com/tangramproject/Tangram.Cypher/initial/LICENSE")
                },
                Title = "CYP Node API",
                Version = CypherNetwork.Helper.Util.GetAssemblyVersion(),
                Description = "Cypher is an early, unaudited, and unrefined release of Tangram. Cypher will serve as a testing ground, allowing teams and developers to build and deploy nodes, send and receive transactions.",
                TermsOfService = new Uri("https://cypherpunks.network/home/privacy/"),
                Contact = new Microsoft.OpenApi.Models.OpenApiContact
                {
                    Url = new Uri("https://cypherpunks.network/")
                }
            });
        });

        return services;
    }
}