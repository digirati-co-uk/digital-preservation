using System.Text.Json;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
using Storage.API.Features.Import.Data;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// MakeActivity hard-coded every object as an ImportJob (its id is actually the Import Job Result's
/// id) and every activity as an Update, even for the job that created the Archival Group. The
/// Preservation API's own stream types objects to match their ids and emits Create when there was
/// no previous version - this brings the Storage stream in line (issue #273 item 4).
/// </summary>
public class ImportJobResultStoreActivityTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private static readonly Uri ResultUri = new("https://storage.test/import/results/job-1");

    [Fact]
    public void A_Job_With_No_Source_Version_And_A_New_Version_Is_A_Create()
    {
        var entity = MakeEntity(sourceVersion: null, newVersion: "v1");

        var activity = ImportJobResultStore.MakeActivity(entity);

        activity.Type.Should().Be(ActivityTypes.Create,
            "a null SourceVersion with a NewVersion means the job created the Archival Group");
    }

    [Fact]
    public void A_Job_With_A_Source_Version_Is_An_Update()
    {
        var entity = MakeEntity(sourceVersion: "v1", newVersion: "v2");

        var activity = ImportJobResultStore.MakeActivity(entity);

        activity.Type.Should().Be(ActivityTypes.Update);
    }

    [Fact]
    public void A_Failed_Job_With_No_New_Version_Is_An_Update()
    {
        var entity = MakeEntity(sourceVersion: null, newVersion: null);

        var activity = ImportJobResultStore.MakeActivity(entity);

        activity.Type.Should().Be(ActivityTypes.Update,
            "there is nothing to show a failed job created anything");
    }

    [Fact]
    public void A_Row_With_No_Stored_Result_Is_An_Update()
    {
        var entity = new ImportJobEntity
        {
            Id = "job-1",
            ArchivalGroup = ArchivalGroup,
            ImportJobJson = "{}",
            ImportJobResultJson = null,
            ImportJobResultUri = ResultUri,
            Received = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };

        var activity = ImportJobResultStore.MakeActivity(entity);

        activity.Type.Should().Be(ActivityTypes.Update);
    }

    [Theory]
    [InlineData(null, "v1")]
    [InlineData("v1", "v2")]
    public void The_Object_Type_Is_ImportJobResult_Not_ImportJob(string? sourceVersion, string? newVersion)
    {
        var entity = MakeEntity(sourceVersion, newVersion);

        var activity = ImportJobResultStore.MakeActivity(entity);

        activity.Object.Type.Should().Be(nameof(ImportJobResult));
        activity.Object.Id.Should().Be(ResultUri, "the object's id is the Import Job Result's own id");
    }

    private static ImportJobEntity MakeEntity(string? sourceVersion, string? newVersion)
    {
        var result = new ImportJobResult
        {
            Id = ResultUri,
            ImportJob = new Uri("https://storage.test/importjobs/job-1"),
            ArchivalGroup = ArchivalGroup,
            Status = ImportJobStates.Completed,
            SourceVersion = sourceVersion,
            NewVersion = newVersion
        };
        return new ImportJobEntity
        {
            Id = "job-1",
            ArchivalGroup = ArchivalGroup,
            ImportJobJson = "{}",
            ImportJobResultJson = JsonSerializer.Serialize(result),
            ImportJobResultUri = ResultUri,
            Received = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };
    }
}
