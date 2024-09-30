using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class IndexModel(IMediator mediator, ILogger<IndexModel> logger) : PageModel
{
    public void OnGet()
    {
        // list deposits
    }




}