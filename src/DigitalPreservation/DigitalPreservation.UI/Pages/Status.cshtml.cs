using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Preservation.Client;
using Storage.Repository.Common;
using Storage.Repository.Common.Requests;

namespace DigitalPreservation.UI.Pages;

public class StatusModel(
    IOptions<PreservationOptions> preservationOptions,
    IOptions<AwsStorageOptions> awsStorageOptions,
    IMediator mediator) : PageModel
{
    [BindProperty] public List<ConnectivityCheckResult> ConnectivityChecks { get; set; } = [];

    public Dictionary<string, string> GetOptions()
    {
        var presOptions = preservationOptions.Value;
        var storageOptions = awsStorageOptions.Value;
        return new Dictionary<string, string>()
        {
            ["Preservation__Root"] = presOptions.Root.ToString(),
            ["AwsStorage__DefaultWorkingBucket"] = storageOptions.DefaultWorkingBucket
        };
    }
    
    public async Task OnGet()
    {
        var backendPreservationAlive = await mediator.Send(new VerifyPreservationRunning());
        ConnectivityChecks.Add(backendPreservationAlive);
        
        var backendPreservationAliveNoAuth = await mediator.Send(new VerifyPreservationRunningNoAuth());
        ConnectivityChecks.Add(backendPreservationAliveNoAuth);

        var uiCanTalkToS3 = await mediator.Send(new VerifyS3Reachable(ConnectivityCheckResult.PreservationUIReadS3));
        ConnectivityChecks.Add(uiCanTalkToS3);
        
        var preservationCanTalkToS3 = await mediator.Send(new VerifyPreservationCanTalkToS3());
        ConnectivityChecks.Add(preservationCanTalkToS3);
        
        var storageCanTalkToS3 = await mediator.Send(new VerifyStorageCanTalkToS3());
        ConnectivityChecks.Add(storageCanTalkToS3);
    }
}