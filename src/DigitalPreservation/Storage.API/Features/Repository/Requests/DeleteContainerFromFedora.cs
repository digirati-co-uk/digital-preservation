using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class DeleteContainerFromFedora(string pathUnderFedoraRoot, bool purge) : IRequest<Result>
{
    public string PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public bool Purge { get; } = purge;
}

public class DeleteContainerFromFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<DeleteContainerFromFedora, Result>
{
    public async Task<Result> Handle(DeleteContainerFromFedora request, CancellationToken cancellationToken)
    {
        // TODO: Set caller identity in Fedora
        return await fedoraClient.DeleteContainerOutsideOfArchivalGroup(request.PathUnderFedoraRoot, request.Purge, cancellationToken: cancellationToken);
    }
}