using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Storage.Requests;
using Storage.Repository.Common;
using Storage.Repository.Common.Requests;

namespace Preservation.API.Features.Storage;

/// <summary>
/// Temporary for connectivity check only
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("[controller]")]
public class StorageController(IMediator mediator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> StorageCheck()
    {
        var res = await mediator.Send(new VerifyStorageRunning());
        return new OkObjectResult(res);
    }

    [HttpGet]
    [Route("check-s3")]
    public async Task<IActionResult> S3Check()
    {
        // Can Preservation API speak to S3?
        var res = await mediator.Send(
            new VerifyS3Reachable(ConnectivityCheckResult.PreservationApiReadS3));
        return new OkObjectResult(res);
    }

    [HttpGet]
    [Route("check-storage-s3")]
    public async Task<IActionResult> S3StorageCheck()
    {
        // Can Storage API speak to S3?
        var res = await mediator.Send(new VerifyStorageCanSeeS3());
        return new OkObjectResult(res);
    }
}