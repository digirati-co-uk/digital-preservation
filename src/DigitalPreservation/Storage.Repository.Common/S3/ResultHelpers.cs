using System.Net;
using Amazon.S3;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;

namespace Storage.Repository.Common.S3;

public class ResultHelpers
{
    public static Result<T?> FailFromS3Exception<T>(AmazonS3Exception s3E, string message, Uri s3Uri)
    {
        return s3E.StatusCode switch
        {
            HttpStatusCode.NotFound => Result.Fail<T>(ErrorCodes.NotFound,
                $"{message} - Not Found for {s3Uri}; {s3E.Message}"),
            HttpStatusCode.Conflict => Result.Fail<T>(ErrorCodes.Conflict,
                $"{message} - Conflicting resource at {s3Uri}; {s3E.Message}"),
            HttpStatusCode.Unauthorized => Result.Fail<T>(ErrorCodes.Unauthorized,
                $"{message} - Unauthorized for {s3Uri}; {s3E.Message}"),
            HttpStatusCode.BadRequest => Result.Fail<T>(ErrorCodes.BadRequest,
                $"{message} - Bad Request for {s3Uri}; {s3E.Message}"),
            _ => Result.Fail<T>(ErrorCodes.UnknownError,
                $"{message} - AWS returned status code {s3E.StatusCode} for {s3Uri} with message {s3E.Message}.")
        };
    }    
    
    public static Result<T> FailNotNullFromS3Exception<T>(AmazonS3Exception s3E, string message, Uri s3Uri)
    {
        return s3E.StatusCode switch
        {
            HttpStatusCode.NotFound => Result.FailNotNull<T>(ErrorCodes.NotFound,
                $"{message} - Not Found for {s3Uri}; {s3E.Message}"),
            HttpStatusCode.Conflict => Result.FailNotNull<T>(ErrorCodes.Conflict,
                $"{message} - Conflicting resource at {s3Uri}; {s3E.Message}"),
            HttpStatusCode.Unauthorized => Result.FailNotNull<T>(ErrorCodes.Unauthorized,
                $"{message} - Unauthorized for {s3Uri}; {s3E.Message}"),
            HttpStatusCode.BadRequest => Result.FailNotNull<T>(ErrorCodes.BadRequest,
                $"{message} - Bad Request for {s3Uri}; {s3E.Message}"),
            _ => Result.FailNotNull<T>(ErrorCodes.UnknownError,
                $"{message} - AWS returned status code {s3E.StatusCode} for {s3Uri} with message {s3E.Message}.")
        };
    }

    public static Result<T?> FailFromAwsStatusCode<T>(HttpStatusCode respHttpStatusCode, string message, Uri s3Uri)
    {
        return respHttpStatusCode switch
        {
            HttpStatusCode.NotFound => Result.Fail<T>(ErrorCodes.NotFound,
                $"{message} - Not Found for {s3Uri}"),
            HttpStatusCode.Conflict => Result.Fail<T>(ErrorCodes.Conflict,
                $"{message} - Conflicting resource at {s3Uri}."),
            HttpStatusCode.Unauthorized => Result.Fail<T>(ErrorCodes.Unauthorized,
                $"{message} - Unauthorized for {s3Uri}."),
            HttpStatusCode.BadRequest => Result.Fail<T>(ErrorCodes.BadRequest,
                $"{message} - Bad Request for {s3Uri}."),
            _ => Result.Fail<T>(ErrorCodes.UnknownError,
                $"{message} - AWS returned status code {respHttpStatusCode} for {s3Uri}.")
        };
    }
    
    public static Result<T> FailNotNullFromAwsStatusCode<T>(HttpStatusCode respHttpStatusCode, string message, Uri s3Uri)
    {
        return respHttpStatusCode switch
        {
            HttpStatusCode.NotFound => Result.FailNotNull<T>(ErrorCodes.NotFound,
                $"{message} - Not Found for {s3Uri}"),
            HttpStatusCode.Conflict => Result.FailNotNull<T>(ErrorCodes.Conflict,
                $"{message} - Conflicting resource at {s3Uri}."),
            HttpStatusCode.Unauthorized => Result.FailNotNull<T>(ErrorCodes.Unauthorized,
                $"{message} - Unauthorized for {s3Uri}."),
            HttpStatusCode.BadRequest => Result.FailNotNull<T>(ErrorCodes.BadRequest,
                $"{message} - Bad Request for {s3Uri}."),
            _ => Result.FailNotNull<T>(ErrorCodes.UnknownError,
                $"{message} - AWS returned status code {respHttpStatusCode} for {s3Uri}.")
        };
    }
}