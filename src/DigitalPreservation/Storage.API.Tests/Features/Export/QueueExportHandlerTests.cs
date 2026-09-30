using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Requests;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// POST /export accepted any S3 destination and copied an Archival Group's files there. The
/// permitted set is the default working bucket plus every KnownClients profile's DepositBucket - no
/// separate allow-list (issue #288).
/// </summary>
public class QueueExportHandlerTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private const string DefaultBucket = "default-working-bucket";
    private const string ProfileBucket = "goobi-deposits";

    [Fact]
    public async Task A_Destination_Outside_Every_Deposit_Bucket_Is_Refused_Without_Persisting_Or_Queuing()
    {
        var (handler, store, queue) = MakeHandler();
        var export = new ExportResource
        {
            ArchivalGroup = ArchivalGroup,
            Destination = new Uri("s3://someone-elses-bucket/export-of-my-ag")
        };

        var result = await handler.Handle(new QueueExport(export), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        A.CallTo(() => store.CreateExportResult(A<string>._, A<ExportResource>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => queue.QueueRequest(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Destination_In_The_Default_Working_Bucket_Is_Queued()
    {
        var (handler, store, queue) = MakeHandler();
        var export = new ExportResource
        {
            ArchivalGroup = ArchivalGroup,
            Destination = new Uri($"s3://{DefaultBucket}/export-of-my-ag")
        };

        var result = await handler.Handle(new QueueExport(export), CancellationToken.None);

        result.Success.Should().BeTrue();
        A.CallTo(() => store.CreateExportResult(A<string>._, A<ExportResource>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => queue.QueueRequest(A<string>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Destination_In_A_Known_Clients_Profile_Bucket_Is_Queued()
    {
        var (handler, store, queue) = MakeHandler();
        var export = new ExportResource
        {
            ArchivalGroup = ArchivalGroup,
            Destination = new Uri($"s3://{ProfileBucket}/export-of-my-ag")
        };

        var result = await handler.Handle(new QueueExport(export), CancellationToken.None);

        result.Success.Should().BeTrue();
        A.CallTo(() => store.CreateExportResult(A<string>._, A<ExportResource>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => queue.QueueRequest(A<string>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static (QueueExportHandler handler, IExportResultStore store, IExportQueue queue) MakeHandler()
    {
        var store = A.Fake<IExportResultStore>();
        A.CallTo(() => store.GetUnfinishedExportsForArchivalGroup(ArchivalGroup, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<string>()));
        A.CallTo(() => store.CreateExportResult(A<string>._, A<ExportResource>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        A.CallTo(() => store.GetExportResult(A<string>._, A<CancellationToken>._))
            .ReturnsLazily((string _, CancellationToken _) => Task.FromResult(Result.OkNotNull<ExportResource?>(new ExportResource
            {
                ArchivalGroup = ArchivalGroup,
                Destination = new Uri($"s3://{DefaultBucket}/export-of-my-ag")
            })));
        var queue = A.Fake<IExportQueue>();
        var identityMinter = A.Fake<IIdentityMinter>();
        A.CallTo(() => identityMinter.MintIdentity(A<string>._, A<Uri>._)).Returns("export-1");
        var clientDirectory = A.Fake<IClientDirectory>();
        A.CallTo(() => clientDirectory.DepositBuckets).Returns([ProfileBucket]);
        var storageOptions = Options.Create(new AwsStorageOptions { DefaultWorkingBucket = DefaultBucket });
        var handler = new QueueExportHandler(
            NullLogger<QueueExportHandler>.Instance, identityMinter, store, MakeConverters(), queue,
            clientDirectory, storageOptions);
        return (handler, store, queue);
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
