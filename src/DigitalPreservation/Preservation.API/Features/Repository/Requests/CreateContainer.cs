using DigitalPreservation.Common.Model;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class CreateContainer(string path, string? title) : IRequest<Container?>
{
    public string Path { get; } = path;
    public string? Title { get; } = title;
}

public class CreateContainerHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<CreateContainer, Container?>
{
    public async Task<Container?> Handle(CreateContainer request, CancellationToken cancellationToken)
    {
        var container = await storageApiClient.CreateContainer(request.Path, request.Title);
        resourceMutator.MutateStorageResource(container); 
        return container;
    }
}