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
            case ErrorCodes.PreconditionFailed:
                // 409, not 412 (issue #266): the controller-level If-Match checks already answer
                // 409 for exactly this condition, and the docs tell callers "a mismatch is 409" -
                // consistency matters more than strict HTTP semantics here, and a client then
                // needs only one handler for a METS write losing a race against another write.
                pd.Status = 409;
                break;
            case ErrorCodes.Unprocessable:
                pd.Status = 422;
                break;
            case ErrorCodes.Tombstone:
                pd.Status = 410;
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