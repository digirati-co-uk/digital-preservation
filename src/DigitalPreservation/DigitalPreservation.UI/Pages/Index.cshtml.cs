using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class IndexModel(IMediator mediator) : PageModel
{
    [BindProperty] public string? Message { get; set; }
    
    public async Task OnGet()
    {
        var isAlive = await mediator.Send(new VerifyPreservationRunning());
        Message = isAlive ? "Successfully pinged Preservation API" : "Unable to ping Preservation API";
    }
}