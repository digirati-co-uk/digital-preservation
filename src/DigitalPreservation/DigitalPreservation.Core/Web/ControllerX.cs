using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ControllerX
{
    public static ActionResult StatusResponseFromResult<T>(this Controller controller, Result<T> result)
    {
        if (result.Success && result.ErrorCode.IsNullOrWhiteSpace())
        {
            return controller.Ok(result.Value);
        }
        return GetActionResult(controller, result.ErrorCode, result.ErrorMessage);
    }

    public static ActionResult StatusResponseFromResult(this Controller controller, Result result)
    {
        if (result.Success && result.ErrorCode.IsNullOrWhiteSpace())
        {
            return controller.Ok();
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