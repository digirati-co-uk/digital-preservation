using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetImportJobResultsForDeposit(string depositId) : IRequest<Result<List<ImportJobResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetImportJobResultsForDepositHandler(
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetImportJobResultsForDeposit, Result<List<ImportJobResult>>>
{
    public async Task<Result<List<ImportJobResult>>> Handle(GetImportJobResultsForDeposit request, CancellationToken cancellationToken)
    {
        var importJobEntities = await dbContext.ImportJobs
            .Where(j => j.Deposit == request.DepositId)
            .OrderBy(j => j.DateSubmitted)
            .ToListAsync(cancellationToken);
        var importJobs = importJobEntities
            .Select(j => JsonSerializer.Deserialize<ImportJobResult>(j.LatestPreservationApiResultJson))
            .OfType<ImportJobResult>()
            .ToList();
        // Results stored before issue #265 was fixed still have a Storage API host baked into
        // ImportJob; repair on read rather than migrating the stored JSON.
        importJobs.ForEach(resourceMutator.RepairStoredImportJobResult);
        return Result.OkNotNull(importJobs);
    }
}