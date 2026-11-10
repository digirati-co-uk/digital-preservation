using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits;
using Preservation.API.Features.Deposits.PipelineDelivery;
using Storage.Repository.Common;

namespace Preservation.API.Features.Identity;

/// <summary>
/// Returns the calling identity as the API resolved it (RFC-0001 §9.1). A self-only diagnostic: it
/// reports the caller's own resolved name, the resolution <c>source</c>, the deposit bucket that
/// would be used for them, and whether deposits there may run the pipeline (issue #410). It sits
/// behind the global AuthorizeFilter when auth is enabled.
/// </summary>
[ApiController]
[Route("whoami")]
public class WhoAmIController(
    IClientDirectory clients,
    IOptions<AwsStorageOptions> storageOptions,
    IOptions<PipelineDeliveryFlags> pipelineDeliveryFlags,
    IOptionsMonitor<FeatureFlagOptions> featureFlags) : ControllerBase
{
    [HttpGet(Name = "WhoAmI")]
    [ProducesResponseType(typeof(WhoAmIResult), 200)]
    public IActionResult Get([FromHeader(Name = AuthFilterIdentifier.MachineHeaderName)] string? header)
    {
        var defaultBucket = storageOptions.Value.DefaultWorkingBucket;
        var result = CallerResolver.Resolve(User, header, clients, defaultBucket,
            featureFlags.CurrentValue.RequireKnownCaller);
        result.PipelineAllowed = PipelinePolicy.ForBucket(result.DepositBucket, defaultBucket,
            pipelineDeliveryFlags.Value.AllowPipelineForCallerBuckets, clients) == PipelineAccess.Permitted;
        return Ok(result);
    }
}
