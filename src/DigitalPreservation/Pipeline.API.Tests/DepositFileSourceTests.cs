using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Storage.Repository.Common;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Issue #410: the pipeline reads a deposit in the default working bucket through the mount, as
/// before, and a deposit in a caller's own bucket from a size-capped scratch copy that is deleted
/// when the job ends. Copying is flag-gated, default off.
/// </summary>
public sealed class DepositFileSourceTests : IDisposable
{
    private const string DefaultBucket = "dev-deposits";
    private const string CallerBucket = "leeds-etheses-deposits";

    private readonly string root = Path.Combine(Path.GetTempPath(), "deposit-file-source-" + Guid.NewGuid().ToString("N"));
    private readonly string mountPath;
    private readonly string scratchPath;
    private readonly IAmazonS3 s3 = A.Fake<IAmazonS3>();

    public DepositFileSourceTests()
    {
        mountPath = Path.Combine(root, "mount");
        scratchPath = Path.Combine(root, "scratch");
        Directory.CreateDirectory(mountPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private DepositFileSource Source(bool allowCallerBuckets = true, long maxBytes = 1024 * 1024, string? scratch = "<default>",
        string? defaultBucket = DefaultBucket) =>
        new(NullLogger<DepositFileSource>.Instance,
            s3,
            Options.Create(new StorageOptions
            {
                FileMountPath = mountPath,
                ScratchCopyPath = scratch == "<default>" ? scratchPath : scratch,
                ScratchCopyMaxBytes = maxBytes
            }),
            Options.Create(new AwsStorageOptions { DefaultWorkingBucket = defaultBucket! }),
            Options.Create(new PipelineToolOptions { DirectorySeparator = Path.DirectorySeparatorChar.ToString() }),
            Options.Create(new PipelineFeatureFlags { AllowPipelineForCallerBuckets = allowCallerBuckets }));

    private void GivenObjects(string bucket, params (string Key, string Content)[] objects)
    {
        A.CallTo(() => s3.ListObjectsV2Async(A<ListObjectsV2Request>.That.Matches(r => r.BucketName == bucket), A<CancellationToken>._))
            .Returns(new ListObjectsV2Response
            {
                S3Objects = objects.Select(o => new S3Object { BucketName = bucket, Key = o.Key, Size = Encoding.UTF8.GetByteCount(o.Content) }).ToList(),
                IsTruncated = false
            });
        foreach (var (key, content) in objects)
        {
            A.CallTo(() => s3.GetObjectAsync(A<GetObjectRequest>.That.Matches(r => r.BucketName == bucket && r.Key == key), A<CancellationToken>._))
                .ReturnsLazily(() => new GetObjectResponse { ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        }
    }

    [Fact]
    public async Task A_Deposit_In_The_Default_Bucket_Is_Read_From_The_Mount_Without_Touching_S3()
    {
        var result = await Source().PrepareAsync(new Uri($"s3://{DefaultBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Value!.Path.Should().Be(mountPath + Path.DirectorySeparatorChar + "dep-1");
        result.Value.ScratchCopy.Should().BeNull("nothing was copied, so there is nothing to delete");
        A.CallTo(s3).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_A_Configured_Default_Bucket_Every_Deposit_Is_Read_From_The_Mount_As_Before(bool allowCallerBuckets)
    {
        // The Pipeline API has never needed AwsStorage:DefaultWorkingBucket; a deployment without
        // it must keep running the pipeline from the mount, not refuse every deposit.
        var result = await Source(allowCallerBuckets, defaultBucket: null)
            .PrepareAsync(new Uri($"s3://{DefaultBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Value!.Path.Should().Be(mountPath + Path.DirectorySeparatorChar + "dep-1");
        A.CallTo(s3).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Missing_Mount_Fails_The_Job()
    {
        Directory.Delete(mountPath);

        var result = await Source().PrepareAsync(new Uri($"s3://{DefaultBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("mount path could not be found");
    }

    [Fact]
    public async Task A_Caller_Bucket_Is_Refused_While_The_Flag_Is_Off()
    {
        var result = await Source(allowCallerBuckets: false)
            .PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain(CallerBucket);
        A.CallTo(s3).MustNotHaveHappened();
        Directory.Exists(scratchPath).Should().BeFalse();
    }

    [Fact]
    public async Task A_Caller_Bucket_Deposit_Is_Copied_With_Its_Layout_And_Deleted_On_Release()
    {
        GivenObjects(CallerBucket,
            ("deposits/dep-1/", ""),
            ("deposits/dep-1/mets.xml", "<mets/>"),
            ("deposits/dep-1/objects/thesis.pdf", "%PDF-1.7"),
            ("deposits/dep-1/objects/appendix/data.csv", "a,b"));
        var source = Source();

        var result = await source.PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var copy = result.Value!.Path;
        copy.Should().Be(Path.Combine(scratchPath, "dep-1"));
        result.Value.ScratchCopy.Should().Be(copy);
        (await File.ReadAllTextAsync(Path.Combine(copy, "mets.xml"), TestContext.Current.CancellationToken)).Should().Be("<mets/>");
        (await File.ReadAllTextAsync(Path.Combine(copy, "objects", "thesis.pdf"), TestContext.Current.CancellationToken)).Should().Be("%PDF-1.7");
        (await File.ReadAllTextAsync(Path.Combine(copy, "objects", "appendix", "data.csv"), TestContext.Current.CancellationToken)).Should().Be("a,b");
        A.CallTo(() => s3.GetObjectAsync(A<GetObjectRequest>.That.Matches(r => r.ChecksumMode == ChecksumMode.ENABLED), A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);

        source.Release(result.Value);

        Directory.Exists(copy).Should().BeFalse("a scratch copy must not outlive its job");
    }

    [Fact]
    public async Task A_Deposit_Larger_Than_The_Cap_Is_Refused_Before_Anything_Is_Downloaded()
    {
        GivenObjects(CallerBucket, ("deposits/dep-1/objects/big.tif", new string('x', 2048)));

        var result = await Source(maxBytes: 1024)
            .PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("larger than");
        A.CallTo(() => s3.GetObjectAsync(A<GetObjectRequest>._, A<CancellationToken>._)).MustNotHaveHappened();
        Directory.Exists(Path.Combine(scratchPath, "dep-1")).Should().BeFalse();
    }

    [Fact]
    public async Task A_Failed_Download_Leaves_No_Partial_Copy_Behind()
    {
        GivenObjects(CallerBucket, ("deposits/dep-1/objects/a.txt", "a"), ("deposits/dep-1/objects/b.txt", "b"));
        A.CallTo(() => s3.GetObjectAsync(A<GetObjectRequest>.That.Matches(r => r.Key.EndsWith("b.txt")), A<CancellationToken>._))
            .Throws(new AmazonS3Exception("checksum mismatch"));

        var result = await Source().PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.UnknownError);
        Directory.Exists(Path.Combine(scratchPath, "dep-1")).Should().BeFalse();
    }

    [Fact]
    public async Task An_Object_Key_That_Would_Escape_The_Copy_Is_Refused()
    {
        GivenObjects(CallerBucket, ("deposits/dep-1/../../outside.txt", "nope"));

        var result = await Source().PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        File.Exists(Path.Combine(root, "outside.txt")).Should().BeFalse();
        File.Exists(Path.Combine(scratchPath, "outside.txt")).Should().BeFalse();
        Directory.Exists(Path.Combine(scratchPath, "dep-1")).Should().BeFalse();
    }

    [Fact]
    public async Task A_Stale_Copy_From_A_Crashed_Job_Is_Replaced_Not_Reused()
    {
        var stale = Path.Combine(scratchPath, "dep-1", "objects");
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "deleted-since.txt"), "old", TestContext.Current.CancellationToken);
        GivenObjects(CallerBucket, ("deposits/dep-1/objects/a.txt", "a"));

        var result = await Source().PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.ErrorMessage);
        File.Exists(Path.Combine(stale, "deleted-since.txt")).Should().BeFalse();
        File.Exists(Path.Combine(stale, "a.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Copying_Without_A_Scratch_Path_Configured_Fails_Clearly()
    {
        var result = await Source(scratch: null)
            .PrepareAsync(new Uri($"s3://{CallerBucket}/deposits/dep-1/"), "dep-1", TestContext.Current.CancellationToken);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("ScratchCopyPath");
    }

    [Fact]
    public void Release_Never_Deletes_Outside_The_Scratch_Directory()
    {
        var outside = Path.Combine(root, "not-scratch");
        Directory.CreateDirectory(outside);

        Source().Release(new DepositRoot(outside, outside));
        Source().Release(new DepositRoot(scratchPath, scratchPath));
        Source().Release(null);

        Directory.Exists(outside).Should().BeTrue();
    }
}
