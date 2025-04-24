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
        //default to root if path is not well-formed
        var validPath = Uri.IsWellFormedUriString(path, UriKind.Relative) ? path : "/";

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
