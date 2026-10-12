using FluentAssertions;
using Pipeline.API.Features.Pipeline.Requests;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// "clamscan --version" normally reports "ClamAV &lt;engine&gt;/&lt;daily-db-version&gt;/&lt;date&gt;",
/// which is what gets written into METS/PREMIS as virus-check provenance. When clamd's
/// VERSION command is disabled (a Debian clamav-daemon default - digital-preservation#363),
/// clamdscan falls back to the bare "ClamAV &lt;engine&gt;" form, silently dropping which
/// signature database a scan used. HasSignatureDatabaseVersion is the check ExecutePipelineJob
/// uses to log a warning instead of letting that regress unnoticed.
/// </summary>
public class VirusDefinitionTests
{
    [Theory]
    [InlineData("ClamAV 1.3.1/27210/Tue Oct  7 08:12:34 2026\n")]
    [InlineData("ClamAV 0.103.11/26800/Mon Jun  2 09:00:00 2025")]
    public void HasSignatureDatabaseVersion_True_ForTheFullForm(string virusDefinition)
    {
        ProcessPipelineJobHandler.HasSignatureDatabaseVersion(virusDefinition).Should().BeTrue();
    }

    [Theory]
    [InlineData("ClamAV 1.5.4\n")]
    [InlineData("ClamAV 1.5.4")]
    [InlineData("")]
    public void HasSignatureDatabaseVersion_False_ForTheBareOrEmptyForm(string virusDefinition)
    {
        ProcessPipelineJobHandler.HasSignatureDatabaseVersion(virusDefinition).Should().BeFalse();
    }
}
