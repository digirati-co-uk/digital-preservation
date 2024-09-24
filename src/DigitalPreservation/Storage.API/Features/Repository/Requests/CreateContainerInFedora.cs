using DigitalPreservation.Common.Model;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class CreateContainerInFedora(string pathUnderFedoraRoot, string? name) : IRequest<Container?>
{
    public string PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public string? Name { get; } = name;
}

public class CreateContainerInFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<CreateContainerInFedora, Container?>
{
    public async Task<Container?> Handle(CreateContainerInFedora request, CancellationToken cancellationToken)
    {
        return await fedoraClient.CreateContainer(request.PathUnderFedoraRoot, request.Name, cancellationToken: cancellationToken);
    }
}