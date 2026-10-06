using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Requests;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Tests.Features.Export;

/// <summary>
/// /exportMetsOnly calls ExecuteExport with a null identifier, so export.Id stays null too. The
/// catch block's error id was built as new Uri(export.Id + "#error") - a bare "#error" is a relative
/// URI, and Uri's constructor throws for it, so an exception during a METS-only export escaped this
/// handler as a 500 instead of the errors array every other export failure is reported in
/// (issue #273 item 2).
/// </summary>
public class ExecuteExportHandlerTests
{
    [Fact]
    public async Task A_Failure_With_No_Identifier_Is_Reported_In_Errors_With_A_Null_Id_Not_Thrown()
    {
        var storageMapper = A.Fake<IStorageMapper>();
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._))
            .Throws(new InvalidOperationException("no such storage map"));
        var handler = new ExecuteExportHandler(
            storageMapper, A.Fake<IAmazonS3>(), A.Fake<IExportResultStore>(), NullLogger<ExecuteExportHandler>.Instance);
        var export = new ExportResource
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Destination = new Uri("s3://export-bucket/somewhere/")
        };

        var act = () => handler.Handle(new ExecuteExport(null, export, metsOnly: true), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeTrue(
            "an export failure is reported in the result's errors, not as a failed Result");
        result.Subject.Value!.Errors.Should().ContainSingle();
        result.Subject.Value.Errors![0].Id.Should().BeNull();
        result.Subject.Value.Errors![0].Message.Should().Be("no such storage map");
    }

    // The copy-and-verify step (CopyAndVerify) was extracted from Handle for clarity; these pin its
    // behaviour: a matching SHA-256 records the copied file, a mismatch is an error for that file only.
    private const string GoodHash = "a3f1c2";
    private const string BadHash = "0000ff";

    [Fact]
    public async Task Each_File_Is_Copied_And_A_Checksum_Mismatch_Is_Reported_Without_Stopping_The_Export()
    {
        var (handler, s3) = HandlerFor(StorageMap(("objects/good.tif", GoodHash), ("objects/bad.tif", GoodHash)));
        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>.That.Matches(r => r.DestinationKey.EndsWith("good.tif")), A<CancellationToken>._))
            .Returns(new CopyObjectResponse { ChecksumSHA256 = Base64Of(GoodHash) });
        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>.That.Matches(r => r.DestinationKey.EndsWith("bad.tif")), A<CancellationToken>._))
            .Returns(new CopyObjectResponse { ChecksumSHA256 = Base64Of(BadHash) });

        var result = await handler.Handle(new ExecuteExport(null, NewExport(), metsOnly: false), CancellationToken.None);

        var export = result.Value!;
        export.Files.Should().ContainSingle().Which.ToString().Should().EndWith("somewhere/objects/good.tif");
        export.Errors.Should().ContainSingle().Which.Message.Should().Contain("does not match expected value");
        export.DateFinished.Should().NotBeNull("a per-file mismatch doesn't abandon the export");
    }

    [Fact]
    public async Task A_Mets_Only_Export_Copies_Only_The_Mets_File()
    {
        var (handler, s3) = HandlerFor(StorageMap(("mets.xml", GoodHash), ("objects/page.tif", GoodHash)));
        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>._, A<CancellationToken>._))
            .Returns(new CopyObjectResponse { ChecksumSHA256 = Base64Of(GoodHash) });

        var result = await handler.Handle(new ExecuteExport(null, NewExport(), metsOnly: true), CancellationToken.None);

        result.Value!.Files.Should().ContainSingle().Which.ToString().Should().EndWith("somewhere/mets.xml");
        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static (ExecuteExportHandler, IAmazonS3) HandlerFor(StorageMap storageMap)
    {
        var storageMapper = A.Fake<IStorageMapper>();
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._)).Returns(storageMap);
        var s3 = A.Fake<IAmazonS3>();
        return (new ExecuteExportHandler(storageMapper, s3, A.Fake<IExportResultStore>(),
            NullLogger<ExecuteExportHandler>.Instance), s3);
    }

    private static ExportResource NewExport() => new()
    {
        ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
        Destination = new Uri("s3://export-bucket/somewhere/")
    };

    private static StorageMap StorageMap(params (string Path, string Hash)[] files)
    {
        var version = new ObjectVersion { MementoTimestamp = "20261006000000", MementoDateTime = DateTime.UtcNow, OcflVersion = "v1" };
        return new StorageMap
        {
            Version = version,
            HeadVersion = version,
            AllVersions = [version],
            StorageType = "S3",
            Root = "ocfl-bucket",
            ObjectPath = "initial/abc",
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Files = files.ToDictionary(f => f.Path, f => new OriginFile { Hash = f.Hash, FullPath = "v1/content/" + f.Path }),
            Hashes = files.ToDictionary(f => f.Path, f => f.Hash)
        };
    }

    private static string Base64Of(string hexHash) => Convert.ToBase64String(Convert.FromHexString(hexHash));
}
