using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Features.Downloads;

namespace Storage.API.Tests.Features.Downloads;

/// <summary>
/// Copies for download (RFC 011): the file is found by its digest, copied into the downloads bucket
/// under the request's id, and kept only if the copy's digest matches.
/// </summary>
public class DownloadCopiesTests
{
    private const string Digest = "a3f1c2";
    private const string DownloadsBucket = "downloads-bucket";

    private readonly IAmazonS3 s3 = A.Fake<IAmazonS3>();
    private readonly IStorageMapper storageMapper = A.Fake<IStorageMapper>();

    [Fact]
    public async Task A_File_Is_Located_By_Its_Digest_In_The_Repository_Bucket()
    {
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._)).Returns(StorageMapWith(Digest));

        var located = await Copies().Locate(CopyOf(Digest));

        located.Success.Should().BeTrue();
        located.Value!.SourceBucket.Should().Be("ocfl-bucket");
        located.Value.SourceKey.Should().Be("initial/abc/v1/content/objects/photo.tiff");
    }

    [Fact]
    public async Task A_Digest_The_Archival_Group_Does_Not_Hold_Is_Not_Found()
    {
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._)).Returns(StorageMapWith(Digest));

        var located = await Copies().Locate(CopyOf("ffffff"));

        located.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task An_Archival_Group_That_Cannot_Be_Read_Is_Not_Found()
    {
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._))
            .Throws(new InvalidOperationException("no inventory"));

        var located = await Copies().Locate(CopyOf(Digest));

        located.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Theory]
    [InlineData("../other", "photo.tiff")]
    [InlineData("", "photo.tiff")]
    [InlineData("abc123", "folder/photo.tiff")]
    [InlineData("abc123", "..")]
    [InlineData("abc123", "")]
    public async Task An_Id_Or_Name_That_Could_Move_The_Copy_Elsewhere_Is_Refused(string id, string name)
    {
        var located = await Copies().Locate(CopyOf(Digest, id, name));

        located.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        A.CallTo(() => storageMapper.GetStorageMap(A<Uri>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task The_Copy_Goes_Under_The_Request_Id_With_The_Files_Content_Type()
    {
        CopyReturnsDigest(Digest);

        await Copies().Copy(JobFor(Digest), CancellationToken.None);

        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>.That.Matches(r =>
                r.SourceBucket == "ocfl-bucket"
                && r.DestinationBucket == DownloadsBucket
                && r.DestinationKey == "abc123/photo.tiff"
                && r.ContentType == "image/tiff"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => s3.DeleteObjectAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Copy_Whose_Digest_Does_Not_Match_Is_Removed()
    {
        CopyReturnsDigest("0000ff");
        TheBucketHolds("abc123/photo.tiff");

        await Copies().Copy(JobFor(Digest), CancellationToken.None);

        A.CallTo(() => s3.DeleteObjectAsync(DownloadsBucket, "abc123/photo.tiff", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Link_Is_Signed_For_The_Copy_As_An_Attachment_With_Its_File_Name()
    {
        TheBucketHolds("abc123/photo.tiff");
        A.CallTo(() => s3.GetPreSignedURLAsync(A<GetPreSignedUrlRequest>._))
            .Returns("https://downloads-bucket.s3.amazonaws.com/abc123/photo.tiff?signature");

        var link = await Copies().GetLink("abc123", CancellationToken.None);

        link!.Url.Host.Should().Be("downloads-bucket.s3.amazonaws.com");
        link.Expires.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(10));
        A.CallTo(() => s3.GetPreSignedURLAsync(A<GetPreSignedUrlRequest>.That.Matches(r =>
                r.BucketName == DownloadsBucket
                && r.Key == "abc123/photo.tiff"
                && r.ResponseHeaderOverrides.ContentDisposition.StartsWith("attachment; filename=photo.tiff"))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task There_Is_No_Link_And_Nothing_Is_Ready_Without_A_Copy()
    {
        TheBucketHolds();

        (await Copies().GetLink("abc123", CancellationToken.None)).Should().BeNull();
        (await Copies().IsReady("abc123", CancellationToken.None)).Should().BeFalse();
    }

    private DownloadCopies Copies() => new(s3, storageMapper,
        Options.Create(new DownloadOptions { Bucket = DownloadsBucket }), NullLogger<DownloadCopies>.Instance);

    private void CopyReturnsDigest(string hexDigest) =>
        A.CallTo(() => s3.CopyObjectAsync(A<CopyObjectRequest>._, A<CancellationToken>._))
            .Returns(new CopyObjectResponse { ChecksumSHA256 = Convert.ToBase64String(Convert.FromHexString(hexDigest)) });

    private void TheBucketHolds(params string[] keys) =>
        A.CallTo(() => s3.ListObjectsV2Async(A<ListObjectsV2Request>.That.Matches(r =>
                r.BucketName == DownloadsBucket && r.Prefix == "abc123/"), A<CancellationToken>._))
            .Returns(new ListObjectsV2Response { S3Objects = keys.Select(key => new S3Object { Key = key }).ToList() });

    private static DownloadCopy CopyOf(string digest, string id = "abc123", string name = "photo.tiff") => new()
    {
        Id = id,
        ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
        Digest = digest,
        Name = name,
        ContentType = "image/tiff"
    };

    private static DownloadCopyJob JobFor(string digest) =>
        new(CopyOf(digest), "ocfl-bucket", "initial/abc/v1/content/objects/photo.tiff");

    private static StorageMap StorageMapWith(string digest)
    {
        var version = new ObjectVersion { MementoTimestamp = "20261120000000", MementoDateTime = DateTime.UtcNow, OcflVersion = "v1" };
        const string pathInObject = "v1/content/objects/photo.tiff";
        return new StorageMap
        {
            Version = version,
            HeadVersion = version,
            AllVersions = [version],
            StorageType = "S3",
            Root = "ocfl-bucket",
            ObjectPath = "initial/abc",
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Files = new Dictionary<string, OriginFile>
            {
                ["objects/photo.tiff"] = new() { Hash = digest, FullPath = pathInObject }
            },
            Hashes = new Dictionary<string, string> { [digest] = pathInObject }
        };
    }
}
