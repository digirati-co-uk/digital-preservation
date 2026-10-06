using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Preservation.API.Features.Deposits.Requests;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// SetMetsETagBestEffort is what CreateDepositBase and PatchDeposit share to populate metsETag on
/// an already-succeeded response (issue #264) - a failed read must give null, never an exception,
/// because the create or patch it's attached to has already happened.
/// </summary>
public class MetsETagXTests
{
    private readonly IMetsParser metsParser = A.Fake<IMetsParser>();
    private static readonly Uri Files = new("s3://deposits/dep-1/deposit-files/");

    private static Deposit Deposit() => new() { Files = Files };

    [Fact]
    public async Task A_Successful_Read_Sets_The_ETag()
    {
        A.CallTo(() => metsParser.GetMetsFileWrapper(Files, false))
            .Returns(Result.OkNotNull(new MetsFileWrapper { ETag = "\"abc123\"" }));
        var deposit = Deposit();

        await metsParser.SetMetsETagBestEffort(deposit, NullLogger.Instance);

        deposit.MetsETag.Should().Be("\"abc123\"");
    }

    [Fact]
    public async Task A_Failed_Read_Leaves_The_ETag_Null_And_Does_Not_Throw()
    {
        A.CallTo(() => metsParser.GetMetsFileWrapper(Files, false))
            .Returns(Result.FailNotNull<MetsFileWrapper>(ErrorCodes.UnknownError, "S3 is away"));
        var deposit = Deposit();

        var act = () => metsParser.SetMetsETagBestEffort(deposit, NullLogger.Instance);

        await act.Should().NotThrowAsync();
        deposit.MetsETag.Should().BeNull();
    }

    [Fact]
    public async Task A_Deposit_With_No_Files_Location_Is_Left_Alone_Without_Calling_The_Parser()
    {
        var deposit = new Deposit { Files = null };

        await metsParser.SetMetsETagBestEffort(deposit, NullLogger.Instance);

        deposit.MetsETag.Should().BeNull();
        A.CallTo(() => metsParser.GetMetsFileWrapper(A<Uri>._, A<bool>._)).MustNotHaveHappened();
    }
}
