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
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> TestArchivalGroupPath(string archivalGroupPath)
    {
        var result = await storageApiClient.TestArchivalGroupPath(archivalGroupPath);
        return this.StatusResponseFromResult(result);
    }
}