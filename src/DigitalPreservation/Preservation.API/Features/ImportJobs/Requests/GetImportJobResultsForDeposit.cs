using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetImportJobResultsForDeposit(string depositId) : IRequest<Result<List<ImportJobResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetImportJobResultsForDepositHandler(
    PreservationContext dbContext) : IRequestHandler<GetImportJobResultsForDeposit, Result<List<ImportJobResult>>>
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
        return Result.OkNotNull(importJobs);
    }
}