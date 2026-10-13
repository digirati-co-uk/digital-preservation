using System.Text;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using FakeItEasy;
using Storage.Repository.Common;

namespace Preservation.API.Tests.Workspace;

/// <summary>
/// Issue #371: a file ClamAV skips for exceeding a configured size limit (reported with
/// AlertExceedsMax as a Heuristics.Limits.Exceeded.* "hit", exactly like a real detection) must be
/// recorded as "not scanned", not as infected - and must not be recorded as clean either, which is
/// what happened before this fix (a skipped file simply never appeared as a hit, so it silently
/// fell into the same bucket as a file that was actually scanned and found clean).
/// </summary>
public class MetadataReaderVirusLimitsExceededTests
{
    private static readonly Uri Root = new("https://example.org/deposits/dep-1");
    private static readonly DateTime ScannedAt = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    private const string SiegfriedCsv =
        """
        filename,filesize,modified,errors,sha256,namespace,id,format,version,mime,class,basis,warning
        /packing/bc-1/objects/clean.txt,12,2026-10-13T08:00:00Z,,abc123,pronom,x-fmt/111,Plain Text File,,text/plain,Text,extension match txt,
        /packing/bc-1/objects/infected.txt,12,2026-10-13T08:00:00Z,,def456,pronom,x-fmt/111,Plain Text File,,text/plain,Text,extension match txt,
        /packing/bc-1/objects/toobig.bin,31457296,2026-10-13T08:00:00Z,,ghi789,pronom,fmt/unknown,,,application/octet-stream,Binary,,
        """;

    // Real clamdscan output format, confirmed against a live clamd 1.4.3 with AlertExceedsMax
    // enabled and a custom test signature: a size-limit skip is reported as an ordinary hit line,
    // indistinguishable in shape from a genuine detection - only the signature name differs.
    private const string VirusLogWithLimitsExceeded =
        """
        /packing/bc-1/objects/infected.txt: Eicar-Signature FOUND
        /packing/bc-1/objects/toobig.bin: Heuristics.Limits.Exceeded.MaxFileSize FOUND
        ----------- SCAN SUMMARY -----------
        Known viruses: 8695693
        Engine version: 1.4.3
        Scanned directories: 1
        Scanned files: 2
        Infected files: 1
        Time: 12.345 sec (0 m 12 s)
        """;

    [Fact]
    public async Task A_File_Over_The_Size_Limit_Is_Recorded_As_Not_Scanned_Not_Infected_Not_Clean()
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(), Root);

        var scan = VirusScanFor(reader, "objects/toobig.bin");

        scan.NotScanned.Should().BeTrue(
            "ClamAV never actually scanned this file - it was skipped for exceeding the size limit");
        scan.HasVirus.Should().BeFalse(
            "a size-limit skip is not a detection, and must not be recorded as one");
        scan.VirusFound.Should().Be("Heuristics.Limits.Exceeded.MaxFileSize FOUND",
            "the raw ClamAV outcome stays visible for audit, same as it would for a genuine hit");
    }

    [Fact]
    public async Task A_Genuine_Detection_Is_Still_Recorded_As_Infected_Not_Not_Scanned()
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(), Root);

        var scan = VirusScanFor(reader, "objects/infected.txt");

        scan.HasVirus.Should().BeTrue();
        scan.NotScanned.Should().BeFalse(
            "a real detection must not be swallowed by the new not-scanned handling");
        scan.VirusFound.Should().Be("Eicar-Signature FOUND");
    }

    [Fact]
    public async Task A_File_Within_The_Limit_And_Clean_Is_Recorded_As_Neither_Infected_Nor_Not_Scanned()
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(), Root);

        var scan = VirusScanFor(reader, "objects/clean.txt");

        scan.HasVirus.Should().BeFalse();
        scan.NotScanned.Should().BeFalse();
    }

    private static VirusScanMetadata VirusScanFor(MetadataReader reader, string localPath)
    {
        var file = new WorkingFile { LocalPath = localPath, Name = localPath.GetSlug() };
        reader.Decorate(file);
        return file.Metadata.OfType<VirusScanMetadata>().Should().ContainSingle().Subject;
    }

    private static IStorage FakeDepositStorage()
    {
        var storage = A.Fake<IStorage>();

        A.CallTo(() => storage.Exists(A<Uri>._)).Returns(false);
        A.CallTo(() => storage.GetListing(A<Uri>._, A<string>._)).Returns(new List<Uri>());
        A.CallTo(() => storage.GetStream(A<Uri>._)).ReturnsLazily(() =>
            Result.FailNotNull<(Stream? ResponseStream, DateTime LastModified)>(ErrorCodes.NotFound, "no such file"));

        var brunnhilde = Root.AppendEscapedSlug("metadata").AppendEscapedSlug("brunnhilde");
        var virusLog = brunnhilde.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt");
        var report = brunnhilde.AppendEscapedSlug("report.html");
        var siegfried = brunnhilde.AppendEscapedSlug("siegfried.csv");

        A.CallTo(() => storage.Exists(report)).Returns(true);

        A.CallTo(() => storage.GetStream(virusLog))
            .ReturnsLazily(() => Found(VirusLogWithLimitsExceeded, ScannedAt));
        A.CallTo(() => storage.GetStream(siegfried))
            .ReturnsLazily(() => Found(SiegfriedCsv, ScannedAt.AddMinutes(-1)));

        return storage;
    }

    private static Result<(Stream? ResponseStream, DateTime LastModified)> Found(string content, DateTime lastModified)
        => Result.OkNotNull<(Stream? ResponseStream, DateTime LastModified)>(
            (new MemoryStream(Encoding.UTF8.GetBytes(content)), lastModified));
}
