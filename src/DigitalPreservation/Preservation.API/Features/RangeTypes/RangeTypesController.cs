using Microsoft.AspNetCore.Mvc;

namespace Preservation.API.Features.RangeTypes;

[Route("range-types")]
[ApiController]
public class RangeTypesController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<string>>(200, "application/json")]
    public IActionResult GetRangeTypes()
    {
        var rangeTypes = configuration.GetSection("RangeTypes").Get<List<string>>() ?? [];
        return Ok(rangeTypes);
    }
}
