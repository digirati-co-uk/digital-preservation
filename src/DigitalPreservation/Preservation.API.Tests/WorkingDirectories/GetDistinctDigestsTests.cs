using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

namespace Preservation.API.Tests.WorkingDirectories;

public class GetDistinctDigestsTests
{
    private static readonly DateTime BagItTimestamp = new(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PipelineTimestamp = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FileModifiedBeforeBagIt = new(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FileModifiedAfterPipeline = new(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc);

    // Non-BagIt baseline

    [Fact]
    public void NoMetadata_ReturnsEmpty()
    {
        var combined = MakeCombined(metsDigest: null, fileDigest: null);
        combined.GetDistinctDigests().Should().BeEmpty();
    }

    [Fact]
    public void MetsDigestOnly_ReturnsMetsDigest()
    {
        var combined = MakeCombined(metsDigest: "abc", fileDigest: null);
        combined.GetDistinctDigests().Should().ContainSingle().Which.Should().Be("abc");
    }

    [Fact]
    public void MetsAndFileDigestMatch_ReturnsSingleDigest()
    {
        var combined = MakeCombined(metsDigest: "abc", fileDigest: "abc");
        combined.GetDistinctDigests().Should().ContainSingle();
    }

    [Fact]
    public void MetsAndFileDigestMismatch_ReturnsTwoDistinctDigests()
    {
        var combined = MakeCombined(metsDigest: "abc", fileDigest: "xyz");
        combined.GetDistinctDigests().Should().HaveCount(2);
    }

    // BagIt, no pipeline

    [Fact]
    public void BagItOnly_NoSiegfried_FreshFile_BagItDigestIncluded()
    {
        // Without pipeline, the fix still correctly includes the BagIt digest for a fresh
        // file (Modified <= BagItTimestamp). The old code never included it (gated on
        // DepositFileFormatMetadata being non-null, which required Siegfried to have run).
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Digest = "bagit-digest",
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "bagit-digest", Timestamp = BagItTimestamp }
            ]
        };
        var metsFile = new WorkingFile { LocalPath = "objects/file.jpg", Digest = "bagit-digest" };
        var combined = new CombinedFile(depositFile, metsFile);

        combined.GetDistinctDigests().Should().ContainSingle();
    }

    [Fact]
    public void BagItOnly_NoSiegfried_BagItMetsDigestMismatch_ReturnsTwoDigests()
    {
        // Fresh file, BagIt digest does not match METS digest — a real mismatch.
        // The fix correctly surfaces this; the old code would have missed it (BagIt
        // digest was never included without pipeline).
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Digest = "bagit-digest",
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "bagit-digest", Timestamp = BagItTimestamp }
            ]
        };
        var metsFile = new WorkingFile { LocalPath = "objects/file.jpg", Digest = "mets-digest" };
        var combined = new CombinedFile(depositFile, metsFile);

        combined.GetDistinctDigests().Should().HaveCount(2);
    }

    // BagIt + pipeline, files unchanged

    [Fact]
    public void BagItAndSiegfried_SameDigest_FileModifiedBeforePipeline_ReturnsSingleDigest()
    {
        // Normal post-pipeline state: files unchanged so BagIt and Siegfried digests agree.
        // File was uploaded before either timestamp, so Modified < both timestamps.
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Digest = "digest-a",
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "digest-a", Timestamp = BagItTimestamp },
                new FileFormatMetadata { Source = "Siegfried", Digest = "digest-a", Timestamp = PipelineTimestamp }
            ]
        };
        var metsFile = new WorkingFile { LocalPath = "objects/file.jpg", Digest = "digest-a" };
        var combined = new CombinedFile(depositFile, metsFile);

        combined.GetDistinctDigests().Should().ContainSingle();
    }

    // Documents that the timestamp assumption is wrong

    [Fact]
    public void GetDigestMetadata_Timestamp_IsMaxAcrossAllSources_NotBagItSpecific()
    {
        // After pipeline runs, GetDigestMetadata().Timestamp reflects the Siegfried
        // (pipeline) timestamp — the Max of all IDigestMetadata entries — not the
        // BagIt manifest's S3 LastModified. The condition in GetDistinctDigests uses
        // this value but assumes it is the BagIt timestamp.
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "digest-a", Timestamp = BagItTimestamp },
                new FileFormatMetadata { Source = "Siegfried", Digest = "digest-a", Timestamp = PipelineTimestamp }
            ]
        };

        var result = depositFile.GetDigestMetadata();
        result!.Timestamp.Should().Be(PipelineTimestamp, "Max() across all IDigestMetadata entries returns the pipeline timestamp");
        result!.Timestamp.Should().NotBe(BagItTimestamp);
    }

    // Documents that GetDigestMetadata throws when BagIt and Siegfried disagree —
    // which is the actual scenario this fix is targeting

    [Fact]
    public void GetDigestMetadata_ThrowsWhenBagItAndSiegfriedDigestsDiffer()
    {
        // GetDigestMetadata() aggregates all IDigestMetadata sources. When BagIt and
        // Siegfried disagree it throws MetadataException. The fix avoids calling
        // GetDigestMetadata() in GetDistinctDigests so this never triggers there.
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedAfterPipeline,
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "old-digest", Timestamp = BagItTimestamp },
                new FileFormatMetadata { Source = "Siegfried", Digest = "new-digest", Timestamp = PipelineTimestamp }
            ]
        };

        var act = () => depositFile.GetDigestMetadata();
        act.Should().Throw<MetadataException>();
    }

    [Fact]
    public void GetDistinctDigests_WhenBagItDigestIsStale_ExcludesBagItDigestAndReturnsSingleDigest()
    {
        // The fix scenario: file was modified after the BagIt manifest was written (pipeline ran).
        // The stale BagIt digest should be excluded; only the METS and actual file digests remain.
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedAfterPipeline,
            Digest = "new-digest",
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "old-digest", Timestamp = BagItTimestamp },
                new FileFormatMetadata { Source = "Siegfried", Digest = "new-digest", Timestamp = PipelineTimestamp }
            ]
        };
        var metsFile = new WorkingFile { LocalPath = "objects/file.jpg", Digest = "new-digest" };
        var combined = new CombinedFile(depositFile, metsFile);

        var digests = combined.GetDistinctDigests();
        digests.Should().ContainSingle().Which.Should().Be("new-digest");
        digests.Should().NotContain("old-digest");
    }

    [Fact]
    public void GetDistinctDigests_WhenBagItDigestIsFresh_IncludesBagItDigest()
    {
        // File was NOT modified after the BagIt manifest — digest is still valid and included.
        var depositFile = new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Digest = "digest-a",
            Metadata =
            [
                new DigestMetadata { Source = "BagIt", Digest = "digest-a", Timestamp = BagItTimestamp }
            ]
        };
        var metsFile = new WorkingFile { LocalPath = "objects/file.jpg", Digest = "digest-a" };
        var combined = new CombinedFile(depositFile, metsFile);

        combined.GetDistinctDigests().Should().ContainSingle();
    }

    private static CombinedFile MakeCombined(string? metsDigest, string? fileDigest)
    {
        var deposit = fileDigest == null ? null : new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Modified = FileModifiedBeforeBagIt,
            Digest = fileDigest
        };
        var mets = metsDigest == null ? null : new WorkingFile
        {
            LocalPath = "objects/file.jpg",
            Digest = metsDigest
        };
        return new CombinedFile(deposit, mets);
    }
}
