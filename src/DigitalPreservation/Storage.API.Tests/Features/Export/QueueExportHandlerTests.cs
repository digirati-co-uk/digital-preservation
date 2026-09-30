using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Requests;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// Export inherits Created/CreatedBy/LastModified/LastModifiedBy from Resource, same as every other
/// resource in the platform, but QueueExportHandler never set them - only Id. Nothing recorded who
/// asked for an export unless they chose to say (issue #273 item 3).
/// </summary>
public class QueueExportHandlerTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private const string CallerIdentity = "caller";

    [Fact]
    public async Task Created_And_LastModified_Are_Stamped_And_CreatedBy_Defaults_To_The_Caller()
    {
        var (handler, store) = MakeHandler();
        var export = new ExportResource
        {
            ArchivalGroup = ArchivalGroup,
            Destination = new Uri("s3://export-bucket/somewhere/")
        };
        var before = DateTime.UtcNow;

        var result = await handler.Handle(new QueueExport(export, CallerIdentity), CancellationToken.None);

        result.Success.Should().BeTrue();
        export.Created.Should().NotBeNull().And.BeOnOrAfter(before);
        export.LastModified.Should().Be(export.Created);
        export.CreatedBy.Should().NotBeNull("no createdBy was supplied, so it must default to the caller");
        export.LastModifiedBy.Should().Be(export.CreatedBy);
        A.CallTo(() => store.CreateExportResult(A<string>._,
                A<ExportResource>.That.Matches(e =>
                    e.Created != null && e.CreatedBy != null && e.LastModifiedBy == e.CreatedBy),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Supplied_CreatedBy_Is_Trusted_Rather_Than_Overwritten()
    {
        var (handler, _) = MakeHandler();
        var suppliedCreatedBy = new Uri("https://storage.test/agents/someone-else");
        var export = new ExportResource
        {
            ArchivalGroup = ArchivalGroup,
            Destination = new Uri("s3://export-bucket/somewhere/"),
            CreatedBy = suppliedCreatedBy
        };

        var result = await handler.Handle(new QueueExport(export, CallerIdentity), CancellationToken.None);

        result.Success.Should().BeTrue();
        export.CreatedBy.Should().Be(suppliedCreatedBy);
        export.LastModifiedBy.Should().Be(suppliedCreatedBy);
    }

    private static (QueueExportHandler handler, IExportResultStore store) MakeHandler()
    {
        var store = A.Fake<IExportResultStore>();
        A.CallTo(() => store.GetUnfinishedExportsForArchivalGroup(ArchivalGroup, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<string>()));
        A.CallTo(() => store.CreateExportResult(A<string>._, A<ExportResource>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        A.CallTo(() => store.GetExportResult(A<string>._, A<CancellationToken>._))
            .ReturnsLazily((string id, CancellationToken _) => Task.FromResult(Result.OkNotNull<ExportResource?>(new ExportResource
            {
                ArchivalGroup = ArchivalGroup,
                Destination = new Uri("s3://export-bucket/somewhere/")
            })));
        var identityMinter = A.Fake<IIdentityMinter>();
        A.CallTo(() => identityMinter.MintIdentity(A<string>._, A<Uri>._)).Returns("export-1");
        var handler = new QueueExportHandler(
            NullLogger<QueueExportHandler>.Instance, identityMinter, store,
            MakeConverters(), A.Fake<IExportQueue>());
        return (handler, store);
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
