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
/// Issue #507: a file is recorded as virus-checked and clean only when ClamAV's log shows a scan
/// that actually ran. Before, every file absent from the log's hits was clean - including when there
/// was no scan at all because ClamAV was missing, or had no signatures to scan with.
/// </summary>
public class MetadataReaderVirusScanRanTests
{
    private static readonly Uri Root = new("https://example.org/deposits/dep-1");

    private const string SiegfriedCsv =
        """
        filename,filesize,modified,errors,sha256,namespace,id,format,version,mime,class,basis,warning
        /packing/bc-1/objects/a.txt,12,2026-11-27T08:00:00Z,,abc123,pronom,x-fmt/111,Plain Text File,,text/plain,Text,extension match txt,
        """;

    private const string ClamscanLog =
        """
        ----------- SCAN SUMMARY -----------
        Known viruses: 8706502
        Engine version: 1.0.8
        Scanned directories: 2
        Scanned files: 1
        Infected files: 0
        Time: 30.862 sec (0 m 30 s)
        """;

    // clamdscan, through the shim on the servers, can write a shorter summary.
    private const string ClamdscanShortLog =
        """
        ----------- SCAN SUMMARY -----------
        Infected files: 0
        Time: 0.004 sec (0 m 0 s)
        Start Date: 2026:11:27 08:58:15
        End Date:   2026:11:27 08:58:15
        """;

    // What Brunnhilde leaves when ClamAV isn't there: its error, no summary.
    private const string NoClamAvLog =
        """
        /bin/sh: 1: clamscan: not found
        """;

    private const string NoSignaturesLog =
        """
        ----------- SCAN SUMMARY -----------
        Known viruses: 0
        Engine version: 1.5.5
        Scanned directories: 0
        Scanned files: 0
        Infected files: 0
        """;

    [Theory]
    [InlineData(ClamscanLog)]
    [InlineData(ClamdscanShortLog)]
    public async Task A_Scan_That_Ran_Records_A_Clean_File_As_Checked(string log)
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(log), Root);

        var scan = ScansFor(reader, "objects/a.txt").Should().ContainSingle().Subject;
        scan.HasVirus.Should().BeFalse();
        scan.NotScanned.Should().BeFalse();
    }

    [Theory]
    [InlineData(NoClamAvLog)]
    [InlineData(NoSignaturesLog)]
    [InlineData("")]
    public async Task Without_A_Scan_No_File_Is_Recorded_As_Checked(string log)
    {
        var reader = await MetadataReader.Create(FakeDepositStorage(log), Root);

        ScansFor(reader, "objects/a.txt").Should().BeEmpty("nothing was scanned, so nothing is clean");
    }

    [Theory]
    [InlineData(ClamscanLog, true)]
    [InlineData(ClamdscanShortLog, true)]
    [InlineData(NoClamAvLog, false)]
    [InlineData(NoSignaturesLog, false)]
    [InlineData("", false)]
    public void ScanRan_Reads_The_Summary(string log, bool ran)
    {
        MetadataReader.ScanRan(log).Should().Be(ran);
    }

    private static List<VirusScanMetadata> ScansFor(MetadataReader reader, string localPath)
    {
        var file = new WorkingFile { LocalPath = localPath, Name = localPath.GetSlug() };
        reader.Decorate(file);
        return file.Metadata.OfType<VirusScanMetadata>().ToList();
    }

    private static IStorage FakeDepositStorage(string virusLog)
    {
        var storage = A.Fake<IStorage>();
        A.CallTo(() => storage.Exists(A<Uri>._)).Returns(false);
        A.CallTo(() => storage.GetListing(A<Uri>._, A<string>._)).Returns(new List<Uri>());
        A.CallTo(() => storage.GetStream(A<Uri>._)).ReturnsLazily(() =>
            Result.FailNotNull<(Stream? ResponseStream, DateTime LastModified)>(ErrorCodes.NotFound, "no such file"));

        var brunnhilde = Root.AppendEscapedSlug("metadata").AppendEscapedSlug("brunnhilde");
        A.CallTo(() => storage.Exists(brunnhilde.AppendEscapedSlug("report.html"))).Returns(true);
        A.CallTo(() => storage.GetStream(brunnhilde.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt")))
            .ReturnsLazily(() => Found(virusLog));
        A.CallTo(() => storage.GetStream(brunnhilde.AppendEscapedSlug("siegfried.csv")))
            .ReturnsLazily(() => Found(SiegfriedCsv));
        return storage;
    }

    private static Result<(Stream? ResponseStream, DateTime LastModified)> Found(string content)
        => Result.OkNotNull<(Stream? ResponseStream, DateTime LastModified)>(
            (new MemoryStream(Encoding.UTF8.GetBytes(content)), new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc)));
}
