using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ProblemDetailsX
{
    private static async Task<ProblemDetails> GetProblemDetails(HttpResponseMessage response, string messageIfNoProblemDetails)
    {
        var statusCode = (int)response.StatusCode;
        var message = messageIfNoProblemDetails;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            if (problem != null)
            {
                return problem;
            }
        }
        catch (Exception e)
        {
            message = $"{message} => {e.Message}";
        }

        return new ProblemDetails
        {
            Status = statusCode,
            Detail = message,
            Title = messageIfNoProblemDetails
        };
    }
    public static async Task<Result<T>> ToFailNotNullResult<T>(this HttpResponseMessage response, string messageIfNoProblemDetails)
    {
        var problem = await GetProblemDetails(response, messageIfNoProblemDetails);
        return problem.ToFailNotNullResult<T>();
    }
    
    public static async Task<Result<T?>> ToFailResult<T>(this HttpResponseMessage response, string messageIfNoProblemDetails)
    {
        var problem = await GetProblemDetails(response, messageIfNoProblemDetails);
        return problem.ToFailResult<T>();
    }

    public static async Task<Result> ToFailResult(this HttpResponseMessage response, string messageIfNoProblemDetails)
    {
        var problem = await GetProblemDetails(response, messageIfNoProblemDetails);
        return problem.ToFailResult();
    }

    public static Result<T> ToFailNotNullResult<T>(this ProblemDetails problemDetails)
    {
        var errorCode = ErrorCodes.GetErrorCode(problemDetails.Status);
        return Result.FailNotNull<T>(errorCode, problemDetails.Detail);
    }

    public static Result<T?> ToFailResult<T>(this ProblemDetails problemDetails)
    {
        var errorCode = ErrorCodes.GetErrorCode(problemDetails.Status);
        return Result.Fail<T>(errorCode, problemDetails.Detail);
    }

    public static Result ToFailResult(this ProblemDetails problemDetails)
    {
        var errorCode = ErrorCodes.GetErrorCode(problemDetails.Status);
        return Result.Fail(errorCode, problemDetails.Detail);
    }

}