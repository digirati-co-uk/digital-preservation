using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class CreateContainerInFedora(string pathUnderFedoraRoot, string callerIdentity, string? name) : IRequest<Result<Container?>>
{
    public string PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public string CallerIdentity { get; } = callerIdentity;
    public string? Name { get; } = name;
}

public class CreateContainerInFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<CreateContainerInFedora, Result<Container?>>
{
    public async Task<Result<Container?>> Handle(CreateContainerInFedora request, CancellationToken cancellationToken)
    {
        return await fedoraClient.CreateContainer(request.PathUnderFedoraRoot, request.CallerIdentity, request.Name, cancellationToken: cancellationToken);
    }
}