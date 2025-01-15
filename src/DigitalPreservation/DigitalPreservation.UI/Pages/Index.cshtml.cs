using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class IndexModel : PageModel
{
    public void OnGet()
    {
        //Refresh login if session is empty
        if (!HttpContext.Session.Keys.Any())
        {
           HttpContext.Response.Redirect("/Account/RefreshLogin");
        }
    }
}