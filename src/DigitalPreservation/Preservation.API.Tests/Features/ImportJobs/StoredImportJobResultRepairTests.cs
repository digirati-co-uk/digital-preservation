using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;
using ImportJobEntity = Preservation.API.Data.Entities.ImportJob;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// A completed job's ImportJobResult is served straight out of the LatestPreservationApiResultJson
/// column, not re-mutated - so a row written before issue #265 was fixed still has a Storage API
/// host baked into its ImportJob property. Both read paths (a single result, and every result for a
/// deposit) must repair that on the way out, without touching the stored JSON itself.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class StoredImportJobResultRepairTests(DatabaseFixture fixture)
{
    private const string DepositId = "repair-dep";

    [Fact]
    public async Task GetImportJobResult_Repairs_A_Stale_Storage_Host_ImportJob_Uri_On_Read()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        const string importJobId = "repair-job-single";
        context.ImportJobs.Add(SeedCompletedImportJob(importJobId));
        await context.SaveChangesAsync();

        var handler = new GetImportJobResultHandler(
            NullLogger<GetImportJobResultHandler>.Instance, context, A.Fake<IStorageApiClient>(), Mutator());

        var result = await handler.Handle(new GetImportJobResult(DepositId, importJobId), default);

        result.Success.Should().BeTrue();
        result.Value!.ImportJob.Should().Be(new Uri("https://preservation.test/importjobs/repair-job-single"),
            "a completed result read from storage must not still point back at Storage API");
    }

    [Fact]
    public async Task GetImportJobResultsForDeposit_Repairs_Every_Stale_ImportJob_Uri_On_Read()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        const string importJobId = "repair-job-list";
        context.ImportJobs.Add(SeedCompletedImportJob(importJobId));
        await context.SaveChangesAsync();

        var handler = new GetImportJobResultsForDepositHandler(context, Mutator());

        var result = await handler.Handle(new GetImportJobResultsForDeposit(DepositId), default);

        result.Success.Should().BeTrue();
        result.Value.Should().ContainSingle(r => r.ImportJob == new Uri("https://preservation.test/importjobs/repair-job-list"));
    }

    private static ImportJobEntity SeedCompletedImportJob(string importJobId)
    {
        var storedResult = new ImportJobResult
        {
            Id = new Uri($"https://preservation.test/deposits/{DepositId}/importjobs/results/{importJobId}"),
            ImportJob = new Uri($"https://storage.test/importjobs/{importJobId}"), // stale: pre-#265 shape
            Deposit = new Uri($"https://preservation.test/deposits/{DepositId}"),
            ArchivalGroup = new Uri("https://preservation.test/repository/cc/thing"),
            Status = ImportJobStates.Completed
        };
        return new ImportJobEntity
        {
            Id = importJobId,
            Deposit = DepositId,
            StorageImportJobResultId = new Uri($"https://storage.test/importjobs/{importJobId}"),
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Status = ImportJobStates.Completed,
            LastUpdated = DateTime.UtcNow,
            ImportJobJson = "{}",
            LatestStorageApiResultJson = "{}",
            LatestPreservationApiResultJson = JsonSerializer.Serialize(storedResult)
        };
    }

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));
}
