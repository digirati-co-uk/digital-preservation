using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ResultX
{
    public static ProblemDetails ToProblemDetails(this Result result, string? title = null)
    {
        var pd = new ProblemDetails();
        switch (result.ErrorCode)
        {
            case ErrorCodes.NotFound:
                pd.Status = 404;
                break;
            case ErrorCodes.Unauthorized:
                pd.Status = 401;
                break;
            case ErrorCodes.BadRequest:
                pd.Status = 400;
                break;
            case ErrorCodes.Conflict:
                pd.Status = 409;
                break;
            case ErrorCodes.Unprocessable:
                pd.Status = 422;
                break;
            default:
                pd.Status = 500;
                break;
        }

        pd.Detail = result.ErrorMessage;
        pd.Title = title ?? "Status " + pd.Status;
        return pd;
    }
    
}