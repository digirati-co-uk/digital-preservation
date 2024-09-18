using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Storage.Repository.Common;
using Storage.Repository.Common.Requests;

namespace DigitalPreservation.UI.Pages;

public class IndexModel(IMediator mediator) : PageModel
{
    [BindProperty] public List<ConnectivityCheckResult> ConnectivityChecks { get; set; } = [];
    
    public async Task OnGet()
    {
        var backendPreservationAlive = await mediator.Send(new VerifyPreservationRunning());
        ConnectivityChecks.Add(backendPreservationAlive);

        var uiCanTalkToS3 = await mediator.Send(new VerifyS3Reachable{Source = ConnectivityCheckResult.PreservationUIReadS3});
        ConnectivityChecks.Add(uiCanTalkToS3);
        
        var preservationCanTalkToS3 = await mediator.Send(new VerifyPreservationCanTalkToS3());
        ConnectivityChecks.Add(preservationCanTalkToS3);
        
        var storageCanTalkToS3 = await mediator.Send(new VerifyStorageCanTalkToS3());
        ConnectivityChecks.Add(storageCanTalkToS3);
    }
}