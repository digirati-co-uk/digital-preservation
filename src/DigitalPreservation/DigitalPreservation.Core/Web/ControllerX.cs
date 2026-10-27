using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ControllerX
{
    public static ActionResult StatusResponseFromResult<T>(this ControllerBase controller, Result<T> result, int successStatusCode = 200, Uri? createdLocation = null)
    {
        if (result.Success && result.ErrorCode.IsNullOrWhiteSpace())
        {
            switch (successStatusCode)
            {
                case 201:
                    if (createdLocation != null)
                    {
                        if (result.Value is Uri)
                        {
                            return controller.Created(createdLocation, null);
                        }
                        return controller.Created(createdLocation, result.Value);
                    }
                    throw new MissingFieldException("201 Created for a return type must have a location");
                case 204:
                    return controller.NoContent();
                default:
                    return controller.Ok(result.Value);
            }
        }
        return GetProblemObjectResult(result);
    }

    public static ActionResult StatusResponseFromResult(this ControllerBase controller, Result result, int successStatusCode = 200)
    {
        if (result.Success && result.ErrorCode.IsNullOrWhiteSpace())
        {
            return successStatusCode switch
            {
                201 => controller.Created(),
                204 => controller.NoContent(),
                _ => controller.Ok()
            };
        }
        return GetProblemObjectResult(result);
    }

    public static ActionResult GetProblemObjectResult(Result result)
    {
        var pd = result.ToProblemDetails();
        var objectResult = new ObjectResult(pd)
        {
            StatusCode = pd.Status
        };
        return objectResult;
    }

    /// <summary>
    /// The one response every IIIF session-token endpoint gives for a missing, expired or
    /// mismatched token (issue #388 item 2.9) - previously hand-rolled three times (twice in
    /// DepositsController, once in MediaController), with differently-worded 401 detail text.
    /// </summary>
    public static ObjectResult IiifSessionExpiredResult(this ControllerBase controller) => controller.Problem(
        title: "Session expired",
        detail: "The IIIF session token has expired or is invalid. Re-open the manifest from the preservation system to get a fresh link.",
        statusCode: 401);
}