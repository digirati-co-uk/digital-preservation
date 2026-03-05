using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetArchiveJobResult(string depositId) : IRequest<Result<ArchiveJobResult>>
{
    public string DepositId { get; } = depositId;
}

public class GetArchiveJobResultHandler(
    IPreservationApiClient preservationApiClient) : IRequestHandler<GetArchiveJobResult, Result<ArchiveJobResult>>
{
    public async Task<Result<ArchiveJobResult>> Handle(GetArchiveJobResult request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetArchiveJobResult(request.DepositId, cancellationToken);
    }
}