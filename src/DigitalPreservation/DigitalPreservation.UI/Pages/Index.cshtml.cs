using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class IndexModel(IMediator mediator) : PageModel
{
    public void OnGet()
    {
    }
}