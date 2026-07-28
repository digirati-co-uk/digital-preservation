using DigitalPreservation.Common.Model;
using Microsoft.AspNetCore.Mvc;

namespace Preservation.API.Features.AccessConditions;

[Route("access-conditions")]
[ApiController]
public class AccessConditionsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<AccessRestriction>>(200, "application/json")]
    public IActionResult GetAccessConditions()
    {
        var conditions = configuration.GetSection("AccessConditions")
            .Get<List<AccessRestriction>>() ?? [];
        return Ok(conditions);
    }
}
