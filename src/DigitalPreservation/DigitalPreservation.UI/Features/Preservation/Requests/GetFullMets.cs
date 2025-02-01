using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetFullMets(Uri depositRoot, string? depositMetsETag) : IRequest<Result<FullMets>>
{
    public Uri DepositRoot { get; } = depositRoot;
    public string? DepositMetsETag { get; } = depositMetsETag;
}

public class GetFullMetsHandler(IMetsManager metsManager) : IRequestHandler<GetFullMets, Result<FullMets>>
{
    public async Task<Result<FullMets>> Handle(GetFullMets request, CancellationToken cancellationToken)
    {
        return await metsManager.GetFullMets(request.DepositRoot, request.DepositMetsETag);
    }
}   