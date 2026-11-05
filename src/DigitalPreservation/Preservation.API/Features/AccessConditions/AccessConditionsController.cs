using DigitalPreservation.Common.Model.AccessConditions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Preservation.API.Features.AccessConditions;

/// <summary>
/// The installation's <see cref="AccessConditionProfile"/> (issue #433): the restriction vocabulary,
/// the rights-statement grouping and the derived conditions, as the UI needs them to render the
/// dropdowns and show a derived value live. Read-only: the profile is deployment configuration.
/// </summary>
[Route("access-conditions")]
[ApiController]
public class AccessConditionsController(IOptions<AccessConditionProfile> profile) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AccessConditionProfile>(200, "application/json")]
    public IActionResult GetAccessConditionProfile()
    {
        return Ok(profile.Value);
    }
}
