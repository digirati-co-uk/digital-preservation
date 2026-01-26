using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using DigitalPreservation.Common.Model;
using DigitalPreservation.XmlGen.Mods.V3;
using Storage.Repository.Common.S3;

namespace Storage.API.Tests.S3;

public class ResultHelpersTests
{

    #region FailFromS3ExceptionTests

    [Fact]
    public void FailFromS3Exception_Should_Map_NotFound_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.NotFound, "not found");
        var result = ResultHelpers.FailFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.NotFound, errorCode);
        Assert.Contains("Not Found", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }

    [Fact]
    public void FailFromS3Exception_Should_Map_Conflict_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.Conflict, "conflict error");
        var result = ResultHelpers.FailFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Conflict, errorCode);
        Assert.Contains("Conflicting resource at", message);
        Assert.Contains(exception.StatusCode.ToString(), message);
    }

    [Fact]
    public void FailFromS3Exception_Should_Map_Unauthorized_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.Unauthorized, "unauthorized");
        var result = ResultHelpers.FailFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Unauthorized, errorCode);
        Assert.Contains("Unauthorized", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }

    [Fact]
    public void FailFromS3Exception_Should_Map_BadRequest_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.BadRequest, "bad request");
        var result = ResultHelpers.FailFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.BadRequest, errorCode);
        Assert.Contains("Bad Request", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }



    [Fact]
    public void FailFromS3Exception_Should_Map_DefaultError_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.Unused, "unauthorized");
        var result = ResultHelpers.FailFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.UnknownError, errorCode);
        Assert.Contains("AWS returned status code", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }

    #endregion


    #region  FailNotNullFromS3Exception

    [Fact]
    public void FailNotNullFromS3Exception_Should_Map_NotFound_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.NotFound, "not found");
        var result = ResultHelpers.FailNotNullFromS3Exception<string>(exception, "Fetch failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.NotFound, errorCode);
        Assert.Contains("Not Found", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }
    [Fact]
    public void FailNotNullFromS3Exception_Should_Map_Conflict_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.Conflict, "conflict error");
        var result = ResultHelpers.FailNotNullFromS3Exception<string>(exception, "Fetch failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.Conflict, errorCode);
        Assert.Contains("Conflicting resource at", message);
        Assert.Contains(exception.StatusCode.ToString(), message);
    }

    [Fact]
    public void FailNotNullFromS3Exception_Should_Map_BadRequest_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.BadRequest, "bad request");
        var result = ResultHelpers.FailNotNullFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.BadRequest, errorCode);
        Assert.Contains("Bad Request", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }


    [Fact]
    public void FailNotNullFromS3Exception_Should_Map_Unauthorizeds()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.Unauthorized, "unauthorized");
        var result = ResultHelpers.FailNotNullFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Unauthorized, errorCode);
        Assert.Contains("Unauthorized", message);
        Assert.Contains(uri.ToString(), message);
        Assert.Contains(exception.Message, message);
    }

    [Fact]
    public void FailNotNullFromS3Exception_Should_Map_Unknown_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var exception = CreateS3Exception(HttpStatusCode.ServiceUnavailable, "service unavailable");
        var result = ResultHelpers.FailNotNullFromS3Exception<string>(exception, "Fetch failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.UnknownError, errorCode);
        Assert.Contains("AWS returned status code", message);
        Assert.Contains(exception.StatusCode.ToString(), message);
    }

    #endregion


    #region FailFromAwsStatusCode

    [Fact]
    public void FailFromAwsStatusCode_Should_Map_NotFound_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailFromAwsStatusCode<string>(HttpStatusCode.NotFound, "Upload failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.NotFound, errorCode);
        Assert.Contains("Not Found", message);
        Assert.Contains(uri.ToString(), message);
    }


    [Fact]
    public void FailFromAwsStatusCode_Should_Map_Conflict_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailFromAwsStatusCode<string>(HttpStatusCode.Conflict, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Conflict, errorCode);
        Assert.Contains("Conflicting resource", message);
        Assert.Contains(uri.ToString(), message);
    }

    [Fact]
    public void FailFromAwsStatusCode_Should_Map_Unauthorized_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailFromAwsStatusCode<string>(HttpStatusCode.Unauthorized, "Upload failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.Unauthorized, errorCode);
        Assert.Contains("Unauthorized", message);
        Assert.Contains(uri.ToString(), message);
    }


    [Fact]
    public void FailFromAwsStatusCode_Should_Map_BadRequest_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailFromAwsStatusCode<string>(HttpStatusCode.BadRequest, "Upload failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.BadRequest, errorCode);
        Assert.Contains("Bad Request", message);
        Assert.Contains(uri.ToString(), message);
    }

    [Fact]
    public void FailFromAwsStatusCode_Should_Map_Unknown_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailFromAwsStatusCode<string>(HttpStatusCode.InternalServerError, "Upload failed", uri);
        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);
        Assert.Equal(ErrorCodes.UnknownError, errorCode);
        Assert.Contains("AWS returned status code", message);
        Assert.Contains(HttpStatusCode.InternalServerError.ToString(), message);
    }

    #endregion

    #region FailNotNullFromAwsStatusCode

    [Fact]
    public void FailNotNullFromAwsStatusCode_Should_Map_NotFound_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailNotNullFromAwsStatusCode<string>(HttpStatusCode.NotFound, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.NotFound, errorCode);
        Assert.Contains("Not Found", message);
        Assert.Contains(uri.ToString(), message);
    }

    [Fact]
    public void FailNotNullFromAwsStatusCode_Should_Map_Conflict_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailNotNullFromAwsStatusCode<string>(HttpStatusCode.Conflict, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Conflict, errorCode);
        Assert.Contains("Conflicting resource", message);
        Assert.Contains(uri.ToString(), message);
    }


    [Fact]
    public void FailNotNullFromAwsStatusCode_Should_Map_Unauthorized_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailNotNullFromAwsStatusCode<string>(HttpStatusCode.Unauthorized, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.Unauthorized, errorCode);
        Assert.Contains("Unauthorized", message);
        Assert.Contains(uri.ToString(), message);
    }



    [Fact]
    public void FailNotNullFromAwsStatusCode_Should_Map_BadRequest_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailNotNullFromAwsStatusCode<string>(HttpStatusCode.BadRequest, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.BadRequest, errorCode);
        Assert.Contains("Bad Request", message);
        Assert.Contains(uri.ToString(), message);
    }


    [Fact]
    public void FailNotNullFromAwsStatusCode_Should_Map_Unknown_Status()
    {
        var uri = new Uri("s3://bucket/key");
        var result = ResultHelpers.FailNotNullFromAwsStatusCode<string>(HttpStatusCode.InternalServerError, "Upload failed", uri);

        var errorCode = ExtractErrorCode(result);
        var message = ExtractMessage(result);

        Assert.Equal(ErrorCodes.UnknownError, errorCode);
        Assert.Contains("AWS returned status code", message);
        Assert.Contains(HttpStatusCode.InternalServerError.ToString(), message);
    }

#endregion


    private static AmazonS3Exception CreateS3Exception(HttpStatusCode statusCode, string message) =>
        new(message, innerException: null, ErrorType.Sender, errorCode: statusCode.ToString(),
            requestId: "request-id", statusCode: statusCode);

    private static string ExtractErrorCode(object result)
    {
        var resultType = result.GetType();
        var directCodeProp = resultType.GetProperty("ErrorCode") ?? resultType.GetProperty("Code");
        var directCode = directCodeProp?.GetValue(result);
        if (directCode is not null)
        {
            return directCode.ToString() ?? throw new InvalidOperationException("Unable to extract error code from result.");
        }

        throw new InvalidOperationException("Unable to extract error code from result.");
    }


    private static string ExtractMessage(object result)
    {
        var resultType = result.GetType();
        var directMsgProp = resultType.GetProperty("Message") ?? resultType.GetProperty("ErrorMessage");
        if (directMsgProp?.GetValue(result) is string directMsg)
        {
            return directMsg;
        }

        throw new InvalidOperationException("Unable to extract message from result.");
    }
}