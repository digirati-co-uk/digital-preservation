using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.Repository.Common;
using Storage.Repository.Common.Requests;

namespace Storage.API.Features.S3Storage;

[ApiController]
[Route("[controller]")]
public class StorageCheckController(IMediator mediator) : Controller
{
    public async Task<IActionResult> S3Check()
    {
        var res = await mediator.Send(new VerifyS3Reachable{Source = ConnectivityCheckResult.StorageApiReadS3});
        return new OkObjectResult(res);
    }
}