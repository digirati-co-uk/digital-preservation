using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Storage.Repository.Common.Requests;

namespace DigitalPreservation.UI.Pages;

public class IndexModel(IMediator mediator) : PageModel
{
    [BindProperty] public List<string> Messages { get; set; } = [];
    
    public async Task OnGet()
    {
        var backendPreservationAlive = await mediator.Send(new VerifyPreservationRunning());
        Messages.Add(backendPreservationAlive ? "Successfully pinged Preservation API" : "Unable to ping Preservation API");

        var uiCanTalkToS3 = await mediator.Send(new VerifyS3Reachable());
        Messages.Add(uiCanTalkToS3 ? "UI application can talk to S3" : "UI application unable to talk to S3");
        
        var preservationCanTalkToS3 = await mediator.Send(new VerifyPreservationCanTalkToS3());
        Messages.Add(uiCanTalkToS3 ? "Preservation API can talk to S3" : "Preservation API  unable to talk to S3");
        
        var storageCanTalkToS3 = await mediator.Send(new VerifyStorageCanTalkToS3());
        Messages.Add(uiCanTalkToS3 ? "Storage API can talk to S3" : "Storage API unable to talk to S3");
    }
}