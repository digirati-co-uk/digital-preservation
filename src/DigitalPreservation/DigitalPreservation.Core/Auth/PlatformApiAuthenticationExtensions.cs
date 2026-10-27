using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

public static class PlatformApiAuthenticationExtensions
{
    /// <summary>
    /// Registers Azure AD JWT bearer authentication for a platform API, bound from the "AzureAd"
    /// configuration section - the chain Preservation.API and Storage.API each wrote out for
    /// themselves (issue #388 item 1.3). Includes token-acquisition-to-downstream-api support and
    /// the in-memory token cache it requires, needed by both hosts' own downstream calls.
    /// </summary>
    public static IServiceCollection AddPlatformApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
        return services;
    }

    /// <summary>
    /// Adds the two global MVC filters a platform API registers when auth is enabled: a bare
    /// <see cref="AuthorizeFilter"/> (equivalent to a global [Authorize]) followed by
    /// <see cref="AuthFilterIdentifier"/>, which depends on running after it since it reads the
    /// caller's already-authenticated identity (issue #388 item 1.3).
    /// </summary>
    public static MvcOptions AddPlatformAuthorizationFilters(this MvcOptions options)
    {
        options.Filters.Add(new AuthorizeFilter());
        options.Filters.Add(new AuthFilterIdentifier());
        return options;
    }
}
