using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// GetErrorCode is meant to be the single place an HTTP status code becomes an ErrorCode (issue
/// #388 item 2.1) - before the fix, four switch tables in Storage.Repository.Common/S3/ResultHelpers
/// and one in Storage.API/Fedora/FedoraClient each hand-rolled their own, smaller version of this
/// mapping, and had drifted from it and from each other: 412 was recognised in only one of the four,
/// and 403, 410 and 422 fell through to UnknownError in all of them instead of the specific codes
/// pinned below.
/// <para>
/// 403 maps to <see cref="ErrorCodes.UpstreamUnauthorized"/>, not <see cref="ErrorCodes.Unauthorized"/>
/// (review feedback on #394): every caller of this method is translating the result of an outbound
/// call - to S3, Fedora, or a peer platform API - not deciding whether to accept the inbound caller,
/// which is handled earlier by auth middleware and never reaches here. A 403 from something we
/// called means our own credentials were refused, not the caller's; collapsing it into the same
/// code an inbound caller's own bad token produces would tell them their token is the problem when
/// the fault is ours.
/// </para>
/// </summary>
public class ErrorCodesTests
{
    [Theory]
    [InlineData(400, ErrorCodes.BadRequest)]
    [InlineData(401, ErrorCodes.Unauthorized)]
    [InlineData(403, ErrorCodes.UpstreamUnauthorized)]
    [InlineData(404, ErrorCodes.NotFound)]
    [InlineData(409, ErrorCodes.Conflict)]
    [InlineData(410, ErrorCodes.Tombstone)]
    [InlineData(412, ErrorCodes.PreconditionFailed)]
    [InlineData(422, ErrorCodes.Unprocessable)]
    public void Known_Status_Codes_Map_To_Their_ErrorCode(int statusCode, string expectedErrorCode)
    {
        ErrorCodes.GetErrorCode(statusCode).Should().Be(expectedErrorCode);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(null)]
    public void Everything_Else_Maps_To_UnknownError(int? statusCode)
    {
        ErrorCodes.GetErrorCode(statusCode).Should().Be(ErrorCodes.UnknownError);
    }
}
