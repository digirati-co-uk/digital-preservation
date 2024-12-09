using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace DigitalPreservation.UI.Controllers;

public class AccountController : Controller
{

    [HttpGet()]
    [Route("/Account/SignedOut")]
    public IActionResult SignedOut()
    {
        if (AppServicesAuthenticationInformation.IsAppServicesAadAuthenticationEnabled)
        {
            if (AppServicesAuthenticationInformation.LogoutUrl != null)
            {
                return LocalRedirect(AppServicesAuthenticationInformation.LogoutUrl);
            }

            return Ok();
        }

        var scheme = OpenIdConnectDefaults.AuthenticationScheme;
        return SignOut(
            new AuthenticationProperties
            {
                RedirectUri = "/",
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            scheme);
    }
}
