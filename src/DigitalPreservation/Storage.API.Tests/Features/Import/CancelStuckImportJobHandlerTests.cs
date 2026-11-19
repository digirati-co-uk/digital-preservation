using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// CancelStuckImportJobHandler is a thin pass-through to <see cref="IImportJobResultStore.CancelStuckImportJob"/>,
/// except for one check it owns itself: a reason is required, so the record this leaves behind
/// always explains itself (issue #245's "the audit trail is better than a row update") - the store
/// method takes a non-nullable reason, so an empty one must be refused before it gets there.
/// </summary>
public class CancelStuckImportJobHandlerTests
{
    private readonly IImportJobResultStore importJobResultStore = A.Fake<IImportJobResultStore>();
    private readonly CancelStuckImportJobHandler handler;

    public CancelStuckImportJobHandlerTests()
    {
        handler = new CancelStuckImportJobHandler(importJobResultStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuses_A_Missing_Or_Blank_Reason_Without_Calling_The_Store(string reason)
    {
        var result = await handler.Handle(new CancelStuckImportJob("some-job-id", reason), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        A.CallTo(() => importJobResultStore.CancelStuckImportJob(A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Passes_A_Real_Reason_Through_To_The_Store_And_Returns_Its_Result()
    {
        var expected = Result.OkNotNull(new ImportJobResult
        {
            Id = new Uri("https://storage.test/import/results/some-job-id"),
            ImportJob = new Uri("https://storage.test/import/some-job-id"),
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Status = ImportJobStates.CompletedWithErrors
        });
        A.CallTo(() => importJobResultStore.CancelStuckImportJob("some-job-id", "confirmed dead-lettered", A<CancellationToken>._))
            .Returns(expected);

        var result = await handler.Handle(new CancelStuckImportJob("some-job-id", "confirmed dead-lettered"), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }
}
