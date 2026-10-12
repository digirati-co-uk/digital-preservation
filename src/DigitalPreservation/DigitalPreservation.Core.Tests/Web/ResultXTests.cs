using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;

namespace DigitalPreservation.Core.Tests.Web;

/// <summary>
/// Pins ToProblemDetails' error-code-to-status mapping, including PreconditionFailed's 409
/// (issue #266) - a storage-level METS write race must answer the same status as the
/// controller-level If-Match check for the same condition, not fall through to 500.
/// </summary>
public class ResultXTests
{
    [Theory]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    [InlineData(ErrorCodes.BadRequest, 400)]
    [InlineData(ErrorCodes.Conflict, 409)]
    [InlineData(ErrorCodes.PreconditionFailed, 409)]
    [InlineData(ErrorCodes.Unprocessable, 422)]
    [InlineData(ErrorCodes.Tombstone, 410)]
    [InlineData(ErrorCodes.UnknownError, 500)]
    [InlineData("SomeCodeNobodyMapped", 500)]
    public void ToProblemDetails_Maps_ErrorCode_To_Status(string errorCode, int expectedStatus)
    {
        var result = Result.Fail(errorCode, "x");

        var problemDetails = result.ToProblemDetails();

        problemDetails.Status.Should().Be(expectedStatus);
    }
}
