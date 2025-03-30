using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Web;

public static class ProblemDetailsX
{
    public static async Task<Result<T>> ToFailNotNullResult<T>(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return problem!.ToFailNotNullResult<T>();
    }
    
    public static async Task<Result<T?>> ToFailResult<T>(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return problem!.ToFailResult<T>();
    }

    public static async Task<Result> ToFailResult(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return problem!.ToFailResult();
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