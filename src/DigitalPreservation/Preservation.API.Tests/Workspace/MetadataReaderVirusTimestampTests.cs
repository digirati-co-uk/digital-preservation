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
/// A virus scan becomes a PREMIS event, and an event's date is the date of the event. Reading the
/// scan output again - which happens on every pipeline run, and again on a redelivered one - must not
/// change it, or two readings of one scan look like two scans (issue #221).
/// </summary>
public class MetadataReaderVirusTimestampTests
{
    private static readonly Uri Root = new("https://example.org/deposits/dep-1");

    private static readonly DateTime ScannedAt = new(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);

    private const string SiegfriedCsv =
        """
        filename,filesize,modified,errors,sha256,namespace,id,format,version,mime,class,basis,warning
        /packing/bc-1/objects/a.txt,12,2026-06-16T12:00:00Z,,abc123,pronom,x-fmt/111,Plain Text File,,text/plain,Text,extension match txt,
        """;

    // Brunnhilde's ClamAV log for a scan that found nothing.
    private const string CleanVirusLog =
        """
        ----------- SCAN SUMMARY -----------
        Known viruses: 8695693
        Engine version: 0.103.11
        Scanned directories: 1
        Scanned files: 1
        Infected files: 0
        Time: 12.345 sec (0 m 12 s)
        """;

    [Fact]
    public async Task Virus_Scan_Metadata_Is_Timestamped_When_The_Scan_Ran()
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(), Root);

        var scan = VirusScanFor(reader, "objects/a.txt");

        scan.Timestamp.Should().Be(ScannedAt,
            "the ClamAV log is written by the scan, so its last-modified time is when the scan ran");
    }

    [Fact]
    public async Task Reading_The_Same_Scan_Again_Gives_The_Same_Timestamp()
    {
        // The reading that matters: a redelivered pipeline job reads the same unchanged output. If
        // the timestamp moved with the reading, the two events would be indistinguishable from two
        // real scans and there would be nothing to deduplicate on.
        var storage = FakeDepositStorage();

        var first = VirusScanFor(await MetadataReader.Create(storage, Root), "objects/a.txt");
        await Task.Delay(10);
        var second = VirusScanFor(await MetadataReader.Create(storage, Root), "objects/a.txt");

        second.Timestamp.Should().Be(first.Timestamp);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(5L * TimeSpan.TicksPerHour)]
    public async Task A_Scan_Whose_Time_We_Do_Not_Know_Is_Left_Unstamped(long lastModifiedTicks)
    {
        // Two shapes of "S3 did not tell us": an unset value, and the same unset value after
        // Storage.GetStream's ToUniversalTime() has shifted it off exactly-default on a host west
        // of UTC. Both must be recognised, or the second writes a scan dated to the year 1.
        var reader = await MetadataReader.Create(
            FakeDepositStorage(new DateTime(lastModifiedTicks, DateTimeKind.Utc)), Root);

        var scan = VirusScanFor(reader, "objects/a.txt");

        // Deliberately NOT the time we read it. That value looks real, passes MetadataManager's
        // "do we have a scan time?" guard, and is different on every read - so the dedup would
        // silently never match and every redelivery would append another event, which is the whole
        // of issue #221 coming back with nothing to show it. An unset timestamp instead routes this
        // to the writer's explicit branch, which prefers a duplicate event to a lost one.
        scan.Timestamp.Should().Be(default,
            "an unknown scan time must stay unknown rather than become the time we happened to read it");
    }

    private static VirusScanMetadata VirusScanFor(MetadataReader reader, string localPath)
    {
        var file = new WorkingFile { LocalPath = localPath, Name = localPath.GetSlug() };
        reader.Decorate(file);
        return file.Metadata.OfType<VirusScanMetadata>().Should().ContainSingle().Subject;
    }

    private static IStorage FakeDepositStorage(DateTime? virusLogLastModified = null)
    {
        var storage = A.Fake<IStorage>();

        // Defaults first: nothing exists, nothing is listed, nothing can be read. The specific
        // configurations below then describe only the deposit this test is about.
        A.CallTo(() => storage.Exists(A<Uri>._)).Returns(false);
        A.CallTo(() => storage.GetListing(A<Uri>._, A<string>._)).Returns(new List<Uri>());
        A.CallTo(() => storage.GetStream(A<Uri>._)).ReturnsLazily(() =>
            Result.FailNotNull<(Stream? ResponseStream, DateTime LastModified)>(ErrorCodes.NotFound, "no such file"));

        var brunnhilde = Root.AppendEscapedSlug("metadata").AppendEscapedSlug("brunnhilde");
        var virusLog = brunnhilde.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt");
        var report = brunnhilde.AppendEscapedSlug("report.html");
        var siegfried = brunnhilde.AppendEscapedSlug("siegfried.csv");

        // report.html is the probe MetadataReader uses to decide Brunnhilde output is present at all.
        A.CallTo(() => storage.Exists(report)).Returns(true);

        // Streams are created per call: they are read to the end, and MetadataReader may ask for the
        // virus log more than once.
        A.CallTo(() => storage.GetStream(virusLog))
            .ReturnsLazily(() => Found(CleanVirusLog, virusLogLastModified ?? ScannedAt));
        A.CallTo(() => storage.GetStream(siegfried))
            .ReturnsLazily(() => Found(SiegfriedCsv, ScannedAt.AddMinutes(-1)));

        return storage;
    }

    private static Result<(Stream? ResponseStream, DateTime LastModified)> Found(string content, DateTime lastModified)
        => Result.OkNotNull<(Stream? ResponseStream, DateTime LastModified)>(
            (new MemoryStream(Encoding.UTF8.GetBytes(content)), lastModified));
}
