using Microsoft.OpenApi;

namespace DigitalPreservation.Core.Web;

public static class SwaggerGenServiceCollectionX
{
    /// <summary>
    /// Registers Swagger generation with the Bearer (when auth is enabled) and X-Client-Identity
    /// security schemes shared by every API in this solution.
    /// </summary>
    /// <param name="services">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="apiTitle">Title shown for this API in the generated Swagger document</param>
    /// <param name="useAuthFeatureFlag">Whether the Bearer security scheme should be advertised</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddApiSwaggerGen(this IServiceCollection services, string apiTitle,
        bool useAuthFeatureFlag)
        => services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = apiTitle, Version = "v1" });

            if (useAuthFeatureFlag)
            {
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    Description = "Bearer token add:   'Bearer <your token>'  "
                });

                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            }

            c.AddSecurityDefinition("X-Client-Identity", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Client-Identity",
                Description = "client identity header for machine to machine calls"
            });

            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("X-Client-Identity", document)] = []
            });
        });
}
