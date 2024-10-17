using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ControllerX
{
    public static ActionResult StatusResponseFromResult<T>(this Controller controller, Result<T> result, int successStatusCode = 200, Uri? createdLocation = null)
    {
        if (result.Success && result.ErrorCode.IsNullOrWhiteSpace())
        {
            switch (successStatusCode)
            {
                case 201:
                    if (createdLocation != null)
                    {
                        return controller.Created(createdLocation, result.Value);
                    }
                    throw new MissingFieldException("201 Created for a return type must have a location");
                case 204:
                    return controller.NoContent();
                default:
                    return controller.Ok(result.Value);
            }
        }
        return GetActionResult(controller, result.ErrorCode, result.ErrorMessage);
    }

    public static ActionResult StatusResponseFromResult(this Controller controller, Result result, int successStatusCode = 200)
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
        return GetActionResult(controller, result.ErrorCode, result.ErrorMessage);
    }
    
    private static ActionResult GetActionResult(Controller controller, string? errorCode, string? errorMessage)
    {
        if (errorCode.IsNullOrWhiteSpace())
        {
            throw new InvalidOperationException("Can't return an error StatusCodeResult without a code");
        }

        return errorCode switch
        {
            ErrorCodes.NotFound => controller.NotFound(),
            ErrorCodes.Conflict => controller.Conflict(),
            ErrorCodes.Unauthorized => controller.Unauthorized(),
            ErrorCodes.BadRequest => controller.BadRequest(),
            _ => controller.Problem(statusCode: 500, detail: errorMessage, title: errorCode)
        };
    }
}