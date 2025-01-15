using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace DigitalPreservation.UI.Controllers;

public class AccountController(ITokenAcquisition tokenAcquisition) : Controller
{

    [HttpGet()]
    [Route("/Account/SignedOut")]
    public IActionResult SignedOut()
    {
       // string[] scopes = ["user.read"];
       // string accessToken =  tokenAcquisition.GetAccessTokenForUserAsync(scopes).Result;

        //return Ok();
        
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

    [HttpGet()]
    [Route("/Account/RefreshLogin")]
    public IActionResult SignRefresh()
    {
        //issue a challenge to the user to sign in again
        var scheme = OpenIdConnectDefaults.AuthenticationScheme;
        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = "/",
            },
            scheme);
    }
}
