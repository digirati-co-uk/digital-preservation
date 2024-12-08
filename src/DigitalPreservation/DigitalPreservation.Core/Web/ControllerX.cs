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
        return GetProblemObjectResult(result);
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

    
    
}