using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// QueueImportJobHandler runs PreProcessValidateImportJob before saving or queuing anything, so a
/// job that fails that check - a rename, among other things (issue #260) - is refused with 400
/// without ever touching the store or the queue.
/// </summary>
public class QueueImportJobHandlerTests
{
    private readonly IImportJobResultStore importJobResultStore = A.Fake<IImportJobResultStore>();
    private readonly IImportJobQueue importJobQueue = A.Fake<IImportJobQueue>();
    private readonly QueueImportJobHandler handler;

    public QueueImportJobHandlerTests()
    {
        var converters = new Converters(
            Options.Create(new FedoraOptions
            {
                Root = new Uri("https://fedora.test/fcrepo/rest/"),
                AdminUser = "admin",
                AdminPassword = "admin",
                Bucket = "fedora-bucket",
                OcflS3Prefix = ""
            }),
            Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/repository/") }));
        handler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance,
            converters,
            A.Fake<IIdentityMinter>(),
            importJobResultStore,
            importJobQueue);
    }

    [Fact]
    public async Task A_Job_With_A_Rename_Is_Refused_And_Nothing_Is_Saved_Or_Queued()
    {
        var job = new ImportJob
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Deposit = new Uri("https://preservation.test/deposits/dep-1")
        };
        job.BinariesToRename.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/thing/objects/page-001.tif") });

        var result = await handler.Handle(new QueueImportJob(job), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("Renaming is not supported yet");
        A.CallTo(importJobResultStore).MustNotHaveHappened();
        A.CallTo(importJobQueue).MustNotHaveHappened();
    }
}
