using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders;

namespace DigitalPreservation.UI.Infrastructure;

/// <summary>
/// This fixes a bug in MS Auth with DelegatingHandler and token generation
/// </summary>
public class RejectSessionCookieWhenAccountNotInCacheEvents : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var msalTokenCacheProvider = context.HttpContext.RequestServices.GetRequiredService<IMsalTokenCacheProvider>();
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

        /* Build a confidential application to be able to access the token cache. */

        ConfidentialClientApplicationOptions options = new();
        configuration.GetSection(Constants.AzureAd).Bind(options);

        var app = ConfidentialClientApplicationBuilder
            .CreateWithApplicationOptions(options)
            .Build();

        /* Check in the cache if the current user is present. */

        msalTokenCacheProvider.Initialize(app.UserTokenCache);
        var accountId = (context.Principal ?? throw new InvalidOperationException("Missing cookie principal")).GetMsalAccountId();
        var account = await app.GetAccountAsync(accountId);
        if (account == null)
        {
            /* The current user is not present in the token cache, and needs to authenticate again in order to get security tokens. */
            context.RejectPrincipal();
        }
    }
}