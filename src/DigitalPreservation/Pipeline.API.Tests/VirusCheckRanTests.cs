using FluentAssertions;
using Pipeline.API.Features.Pipeline.Requests;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Issue #507: a pipeline run whose virus check didn't happen ends CompletedWithErrors saying so,
/// rather than Completed with every file recorded as clean. VirusCheckProblem is the check it makes
/// on Brunnhilde's ClamAV log.
/// </summary>
public sealed class VirusCheckRanTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "virus-check-ran-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(folder, "viruscheck-log.txt");

    public VirusCheckRanTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void A_Scan_That_Ran_Is_No_Problem()
    {
        File.WriteAllText(LogPath, """
            ----------- SCAN SUMMARY -----------
            Known viruses: 8706502
            Engine version: 1.5.4
            Scanned files: 4
            Infected files: 0
            """);

        ProcessPipelineJobHandler.VirusCheckProblem(LogPath).Should().BeNull();
    }

    [Fact]
    public void No_Log_Means_No_Virus_Check()
    {
        ProcessPipelineJobHandler.VirusCheckProblem(LogPath).Should().Contain("didn't run").And.Contain("no log");
    }

    [Theory]
    [InlineData("/bin/sh: 1: clamscan: not found")]
    [InlineData("----------- SCAN SUMMARY -----------\nKnown viruses: 0\nEngine version: 1.5.5\nInfected files: 0")]
    [InlineData("")]
    public void A_Log_With_No_Scan_In_It_Means_No_Virus_Check(string log)
    {
        File.WriteAllText(LogPath, log);

        ProcessPipelineJobHandler.VirusCheckProblem(LogPath).Should().Contain("didn't run").And.Contain("no scan");
    }
}
