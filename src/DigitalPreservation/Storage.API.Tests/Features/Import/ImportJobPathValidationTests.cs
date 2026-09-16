using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// An Import Job's paths arrive in the request body, so the route-value filter never sees them.
/// These pin that the job's own validation refuses a path that could leave the repository root -
/// before anything is queued (the queue handler calls this for a 400) and before a Fedora
/// transaction is opened (the executor calls it again), because past that point the same path would
/// make GetFedoraUri throw with a transaction open.
/// </summary>
public class ImportJobPathValidationTests
{
    private static ImportJob Job(string archivalGroup) => new()
    {
        ArchivalGroup = new Uri(archivalGroup),
        Deposit = new Uri("https://preservation.test/deposits/dep-1")
    };

    [Fact]
    public void A_Job_Naming_A_Group_Under_The_Root_Passes()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.BinariesToAdd.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/thing/objects/page-001.tif") });
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://storage.test/repository/cc/thing/objects") });

        ExecuteImportJobHandler.PreProcessValidateImportJob(job).Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://storage.test/repository/../fcr:tx")]
    [InlineData("https://storage.test/repository/cc/a%2Fb")]
    [InlineData("https://storage.test/repository//evil")]
    public void A_Job_Whose_Archival_Group_Would_Leave_The_Root_Is_Refused(string archivalGroup)
    {
        // Uri may have canonicalised the dot segments already; either way the path that reaches
        // Fedora must be judged as it will be resolved, and refused here rather than thrown on later.
        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(Job(archivalGroup));

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }

    [Fact]
    public void A_Binary_Id_That_Would_Leave_The_Root_Is_Refused()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.BinariesToAdd.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/thing/objects/a%2Fb") });

        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(job);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Binary ID");
    }

    [Fact]
    public void A_Container_Id_That_Would_Leave_The_Root_Is_Refused()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://storage.test/repository/cc/thing/a%5Cb") });

        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(job);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Container ID");
    }

    [Fact]
    public void A_Binary_In_Another_Archival_Group_Is_Refused()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/someone-elses-thing/objects/x.tif") });

        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(job);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("not within the job's Archival Group");
    }

    [Fact]
    public void A_Container_In_A_Sibling_Group_Sharing_A_Name_Prefix_Is_Refused()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://storage.test/repository/cc/thing2/objects") });

        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(job);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("not within the job's Archival Group");
    }

    [Fact]
    public void The_Archival_Group_Itself_Is_Not_A_Valid_Resource_Id()
    {
        var job = Job("https://storage.test/repository/cc/thing");
        job.BinariesToAdd.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/thing") });

        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(job);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("not within the job's Archival Group");
    }

    [Fact]
    public void A_Resource_On_The_Preservation_Host_Under_The_Same_Group_Passes()
    {
        // Compared by repository path, so a job whose ids carry a different host is not refused for it.
        var job = Job("https://storage.test/repository/cc/thing");
        job.BinariesToAdd.Add(new Binary { Id = new Uri("https://preservation.test/repository/cc/thing/objects/x.tif") });

        ExecuteImportJobHandler.PreProcessValidateImportJob(job).Success.Should().BeTrue();
    }

    [Fact]
    public void A_Job_With_No_Archival_Group_Is_Refused()
    {
        var result = ExecuteImportJobHandler.PreProcessValidateImportJob(new ImportJob());

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }
}
