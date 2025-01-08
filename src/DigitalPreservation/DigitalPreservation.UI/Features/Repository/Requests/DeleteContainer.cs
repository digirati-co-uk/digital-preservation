using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class DeleteContainer(string pathUnderRoot, bool purge) : IRequest<Result>
{
    public string PathUnderRoot { get; } = pathUnderRoot;
    public bool Purge { get; } = purge;
}

public class DeleteContainerHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<DeleteContainer, Result>
{
    public async Task<Result> Handle(DeleteContainer request, CancellationToken cancellationToken)
    {
        var path = StringUtils.BuildPath(false, 
            PreservedResource.BasePathElement, request.PathUnderRoot);
        var result = await preservationApiClient.DeleteContainer(path, request.Purge, cancellationToken);
        return result;
    }
}  