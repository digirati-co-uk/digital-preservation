using DigitalPreservation.Core.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Storage.Repository.Common;

namespace Storage.API.Features.Identity;

/// <summary>
/// Returns the calling identity as the API resolved it (RFC-0001 §9.1). A self-only diagnostic: it
/// reports the caller's own resolved name, the resolution <c>source</c>, and the deposit bucket that
/// would be used for them. It sits behind the global AuthorizeFilter when auth is enabled.
/// </summary>
[ApiController]
[Route("whoami")]
public class WhoAmIController(IClientDirectory clients, IOptions<AwsStorageOptions> storageOptions) : ControllerBase
{
    [HttpGet(Name = "WhoAmI")]
    [ProducesResponseType(typeof(WhoAmIResult), 200)]
    public IActionResult Get()
    {
        var header = Request.Headers.TryGetValue(AuthFilterIdentifier.MachineHeaderName, out var value)
            ? value.FirstOrDefault()
            : null;

        var result = CallerResolver.Resolve(User, header, clients, storageOptions.Value.DefaultWorkingBucket);
        return Ok(result);
    }
}
