using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace DigitalPreservation.UI.Controllers;

public class AccountController() : Controller
{

    [HttpGet()]
    [Route("/Account/SignedOut")]
    public async Task<IActionResult> SignedOut()
    {
        if (AppServicesAuthenticationInformation.IsAppServicesAadAuthenticationEnabled)
        {
            if (AppServicesAuthenticationInformation.LogoutUrl != null)
            {
                return LocalRedirect(AppServicesAuthenticationInformation.LogoutUrl);
            }

            return Ok();
        }


        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        
        var scheme = OpenIdConnectDefaults.AuthenticationScheme;
        return SignOut(
            new AuthenticationProperties
            {
                RedirectUri = "/",
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            scheme);
    }

   
    [HttpGet()]
    [Route("/Account/RefreshLogin/")]
    public IActionResult SignRefresh([FromQuery] string? path )
    {
        // Only ever return the user to a page on this site. A well-formed *relative* reference is
        // not enough: "//host/x" is one, and would send a freshly signed-in user to another host.
        var validPath = !string.IsNullOrEmpty(path) && Url.IsLocalUrl(path) ? path : "/";

        //issue a challenge to the user to sign in again
        var scheme = OpenIdConnectDefaults.AuthenticationScheme;
        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = validPath
            },
            scheme);
    }
}
