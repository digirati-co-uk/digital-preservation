using Storage.API.Features.Import;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// ImportJobOptions replaces the raw GetValue calls - each with its own copy of the default - that
/// ImportJobRunner, ImportJobResultStore, ImportJobReapService and the old static
/// ImportJobHeartbeatSettings used to make independently, and the static Validate both Program.cs
/// files had to remember to call by hand (issue #388). These tests pin the defaults (so the move to
/// options-binding can't silently change behaviour for a host with no ImportJobs section configured
/// at all - true of Storage.API.Importer's own appsettings.Example.json) and the validation rule
/// that is now registered once, via AddStorageCore's .Validate(...).ValidateOnStart(), instead of
/// the two hand-written calls it replaces.
/// </summary>
public class ImportJobOptionsTests
{
    [Fact]
    public void Defaults_Match_The_Values_Every_Reader_Used_To_Hardcode()
    {
        var options = new ImportJobOptions();

        options.HeartbeatIntervalSeconds.Should().Be(60);
        options.HeartbeatWindowMinutes.Should().Be(10);
        options.ReapSweepIntervalMinutes.Should().Be(5);
    }

    [Fact]
    public void Default_Values_Are_Safely_Below_The_Window()
    {
        new ImportJobOptions().HeartbeatIntervalIsSafelyBelowWindow().Should().BeTrue();
    }

    [Theory]
    [InlineData(600, 10)] // exactly the window (10 * 60) - not strictly below it
    [InlineData(601, 10)] // past the window entirely
    public void An_Interval_At_Or_Above_The_Window_Is_Not_Safe(int intervalSeconds, int windowMinutes)
    {
        var options = new ImportJobOptions
        {
            HeartbeatIntervalSeconds = intervalSeconds,
            HeartbeatWindowMinutes = windowMinutes
        };

        options.HeartbeatIntervalIsSafelyBelowWindow().Should().BeFalse(
            "a live job's own gaps between heartbeats could be mistaken for abandonment");
    }

    [Fact]
    public void An_Interval_Comfortably_Below_The_Window_Is_Safe()
    {
        var options = new ImportJobOptions { HeartbeatIntervalSeconds = 30, HeartbeatWindowMinutes = 10 };

        options.HeartbeatIntervalIsSafelyBelowWindow().Should().BeTrue();
    }
}
