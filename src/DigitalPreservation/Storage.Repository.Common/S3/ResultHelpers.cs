using System.Net;
using Amazon.S3;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;

namespace Storage.Repository.Common.S3;

public static class ResultHelpers
{
    public static Result<T?> FailFromS3Exception<T>(AmazonS3Exception s3E, string message, Uri s3Uri)
    {
        var detail = s3E.StatusCode switch
        {
            HttpStatusCode.NotFound => $"{message} - Not Found for {s3Uri}; {s3E.Message}",
            HttpStatusCode.Conflict => $"{message} - Conflicting resource at {s3Uri}; {s3E.Message}",
            HttpStatusCode.Unauthorized => $"{message} - Unauthorized for {s3Uri}; {s3E.Message}",
            HttpStatusCode.BadRequest => $"{message} - Bad Request for {s3Uri}; {s3E.Message}",
            _ => $"{message} - AWS returned status code {s3E.StatusCode} for {s3Uri} with message {s3E.Message}."
        };
        return Result.Fail<T>(ErrorCodes.GetErrorCode((int)s3E.StatusCode), detail);
    }

    public static Result<T> FailNotNullFromS3Exception<T>(AmazonS3Exception s3E, string message, Uri s3Uri)
    {
        var detail = s3E.StatusCode switch
        {
            HttpStatusCode.NotFound => $"{message} - Not Found for {s3Uri}; {s3E.Message}",
            HttpStatusCode.Conflict => $"{message} - Conflicting resource at {s3Uri}; {s3E.Message}",
            HttpStatusCode.Unauthorized => $"{message} - Unauthorized for {s3Uri}; {s3E.Message}",
            HttpStatusCode.BadRequest => $"{message} - Bad Request for {s3Uri}; {s3E.Message}",
            // AmazonS3Client throws rather than returning a 412 response, so a conditional write's
            // lost ETag race reaches here as an exception, not an HttpStatusCode to switch on
            // elsewhere (issue #355).
            HttpStatusCode.PreconditionFailed => $"{message} - Supplied ETag did not match for {s3Uri}; {s3E.Message}",
            _ => $"{message} - AWS returned status code {s3E.StatusCode} for {s3Uri} with message {s3E.Message}."
        };
        return Result.FailNotNull<T>(ErrorCodes.GetErrorCode((int)s3E.StatusCode), detail);
    }

    public static Result<T?> FailFromAwsStatusCode<T>(HttpStatusCode respHttpStatusCode, string message, Uri s3Uri)
    {
        var detail = respHttpStatusCode switch
        {
            HttpStatusCode.NotFound => $"{message} - Not Found for {s3Uri}",
            HttpStatusCode.Conflict => $"{message} - Conflicting resource at {s3Uri}.",
            HttpStatusCode.Unauthorized => $"{message} - Unauthorized for {s3Uri}.",
            HttpStatusCode.BadRequest => $"{message} - Bad Request for {s3Uri}.",
            _ => $"{message} - AWS returned status code {respHttpStatusCode} for {s3Uri}."
        };
        return Result.Fail<T>(ErrorCodes.GetErrorCode((int)respHttpStatusCode), detail);
    }

    public static Result<T> FailNotNullFromAwsStatusCode<T>(HttpStatusCode respHttpStatusCode, string message, Uri s3Uri)
    {
        var detail = respHttpStatusCode switch
        {
            HttpStatusCode.NotFound => $"{message} - Not Found for {s3Uri}",
            HttpStatusCode.Conflict => $"{message} - Conflicting resource at {s3Uri}.",
            HttpStatusCode.Unauthorized => $"{message} - Unauthorized for {s3Uri}.",
            HttpStatusCode.BadRequest => $"{message} - Bad Request for {s3Uri}.",
            _ => $"{message} - AWS returned status code {respHttpStatusCode} for {s3Uri}."
        };
        return Result.FailNotNull<T>(ErrorCodes.GetErrorCode((int)respHttpStatusCode), detail);
    }
}