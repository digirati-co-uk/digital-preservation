using DigitalPreservation.Core.Auth;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Requests;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// /exportMetsOnly calls ExecuteExport directly rather than going through QueueExportHandler, so it
/// must apply the destination check itself, before anything runs - not rely on ExecuteExport, which
/// the queued path only runs later (issue #288).
/// </summary>
public class ExportMetsOnlyControllerTests
{
    private const string DefaultBucket = "default-working-bucket";

    [Fact]
    public async Task A_Destination_Outside_Every_Deposit_Bucket_Is_Refused_Without_Calling_ExecuteExport()
    {
        var mediator = A.Fake<IMediator>();
        var controller = MakeController(mediator);
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri("s3://someone-elses-bucket/export-of-my-ag")
        };

        var result = await controller.ExportQueue(export, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        A.CallTo(() => mediator.Send(A<ExecuteExport>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Destination_In_The_Default_Working_Bucket_Calls_ExecuteExport()
    {
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(A<ExecuteExport>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ExportResource
            {
                ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
                Destination = new Uri($"s3://{DefaultBucket}/export-of-my-ag")
            }));
        var controller = MakeController(mediator);
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri($"s3://{DefaultBucket}/export-of-my-ag")
        };

        await controller.ExportQueue(export, CancellationToken.None);

        A.CallTo(() => mediator.Send(A<ExecuteExport>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static ExportMetsOnlyController MakeController(IMediator mediator)
    {
        var clientDirectory = A.Fake<IClientDirectory>();
        A.CallTo(() => clientDirectory.DepositBuckets).Returns([]);
        var storageOptions = Options.Create(new AwsStorageOptions { DefaultWorkingBucket = DefaultBucket });
        return new ExportMetsOnlyController(
            mediator, MakeConverters(), clientDirectory, storageOptions, NullLogger<ExportMetsOnlyController>.Instance);
    }

    private static Converters MakeConverters() => new(
        Options.Create(new FedoraOptions
        {
            Root = new Uri("https://fedora.test/fcrepo/rest/"),
            AdminUser = "admin",
            AdminPassword = "admin",
            Bucket = "fedora-bucket",
            OcflS3Prefix = ""
        }),
        Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") }));
}
