using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;
using Storage.Client;

namespace Preservation.API.Features.Validation;

[Route("[controller]")]
[ApiController]
public class ValidationController(IStorageApiClient storageApiClient) : Controller
{    
    [HttpGet("archivalgroup/{*archivalGroupPath}", Name ="TestArchivalGroupPath")]
    [ProducesResponseType<ArchivalGroup>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    [ProducesResponseType<ProblemDetails>(409, "application/json")]
    public async Task<IActionResult> TestArchivalGroupPath(string archivalGroupPath)
    {
        var result = await storageApiClient.TestArchivalGroupPath(archivalGroupPath);
        return this.StatusResponseFromResult(result);
    }
}