using DigitalPreservation.Common.Model.Import;
using Microsoft.Extensions.Options;
using Preservation.API.Mutation;

namespace Preservation.API.Tests.Mutation;

/// <summary>
/// A Storage API import job id used to leak straight into Preservation API responses via
/// ImportJobResult.ImportJob - a URI no Preservation API caller can resolve (issue #265). These
/// tests cover the two mutator entry points that stop that: the live-mutation path
/// (MutateStorageImportJobResult, exercised by ExecuteImportJob and GetImportJobResult's
/// not-yet-complete branch) and the read-time repair path (RepairStoredImportJobResult, for results
/// serialised into LatestPreservationApiResultJson before this fix existed).
/// </summary>
public class ResourceMutatorImportJobResultTests
{
    private const string DepositId = "dep-1";
    private const string ImportJobId = "job-1";
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private static readonly Uri StorageImportJobId = new("https://storage.test/importjobs/abc123");

    [Fact]
    public void MutateStorageImportJobResult_Rewrites_The_ImportJob_Host_To_Preservation_Api()
    {
        var mutator = Mutator();
        var result = MakeStorageResult();

        mutator.MutateStorageImportJobResult(result, DepositId, ImportJobId);

        result.ImportJob.Should().Be(new Uri("https://preservation.test/importjobs/abc123"),
            "the job's own id is minted by Storage API, same as every other Storage-host URI on this result");
    }

    [Fact]
    public void GetImportJobResultUri_Builds_The_Same_Id_MutateStorageImportJobResult_Sets()
    {
        var mutator = Mutator();
        var result = MakeStorageResult();

        mutator.MutateStorageImportJobResult(result, DepositId, ImportJobId);

        mutator.GetImportJobResultUri(DepositId, ImportJobId).Should().Be(result.Id,
            "the Activity Stream's seeAlso and the result's own id must mint the same URI shape, or a " +
            "reader following seeAlso from the stream lands somewhere other than the result it names");
    }

    [Fact]
    public void RepairStoredImportJobResult_Rewrites_A_Storage_Host_ImportJob_Uri()
    {
        var mutator = Mutator();
        var stored = MakeStorageResult();
        stored.ImportJob = StorageImportJobId; // as it was serialised before issue #265 was fixed

        mutator.RepairStoredImportJobResult(stored);

        stored.ImportJob.Should().Be(new Uri("https://preservation.test/importjobs/abc123"));
    }

    [Fact]
    public void RepairStoredImportJobResult_Leaves_An_Already_Preservation_Host_ImportJob_Uri_Unchanged()
    {
        var mutator = Mutator();
        var stored = MakeStorageResult();
        stored.ImportJob = new Uri("https://preservation.test/importjobs/abc123"); // already repaired/stored post-fix

        mutator.RepairStoredImportJobResult(stored);

        stored.ImportJob.Should().Be(new Uri("https://preservation.test/importjobs/abc123"),
            "repairing an already-correct result must be a no-op, since it runs unconditionally on every read");
    }

    private static ImportJobResult MakeStorageResult() => new()
    {
        ImportJob = StorageImportJobId,
        ArchivalGroup = ArchivalGroup,
        Status = ImportJobStates.Completed
    };

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));
}
