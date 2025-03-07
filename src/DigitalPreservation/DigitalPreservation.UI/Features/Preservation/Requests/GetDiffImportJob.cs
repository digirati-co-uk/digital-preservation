using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetDiffImportJob(string depositId) : IRequest<Result<ImportJob>>
{
    public string DepositId { get; } = depositId;
}

public class GetDiffImportJobHandler(
    
    IPreservationApiClient preservationApiClient) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        var depositResult = await preservationApiClient.GetDeposit(request.DepositId, cancellationToken);
        if (depositResult is { Success: true, Value: not null })
        {
            // extra check
            if (depositResult.Value.ArchivalGroupName.IsNullOrWhiteSpace() ||
                depositResult.Value.ArchivalGroupName == WorkingDirectory.DefaultRootName)
            {
                return Result.FailNotNull<ImportJob>(ErrorCodes.Conflict, "The deposit has no Archival Group Name");
            }
            return await preservationApiClient.GetDiffImportJob(request.DepositId, cancellationToken);
        }
        return Result.FailNotNull<ImportJob>(ErrorCodes.NotFound, $"No deposit was found for {request.DepositId}");
    }
}