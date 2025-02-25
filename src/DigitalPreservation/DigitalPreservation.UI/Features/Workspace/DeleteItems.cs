using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace DigitalPreservation.UI.Features.Workspace;

public class DeleteItems(Uri? depositFiles, DeleteSelection deleteSelection) : IRequest<Result>
{
    public Uri? DepositFiles { get; } = depositFiles;
    public DeleteSelection DeleteSelection { get; } = deleteSelection;
}

public class DeleteItemsHandler : IRequestHandler<DeleteItems, Result>
{
    public Task<Result> Handle(DeleteItems request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}