using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class CreateContainer(string path, string? title) : IRequest<Result<Container?>>
{
    public string Path { get; } = path;
    public string? Title { get; } = title;
}

public class CreateContainerHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<CreateContainer, Result<Container?>>
{
    public async Task<Result<Container?>> Handle(CreateContainer request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.CreateContainer(request.Path, request.Title);
        if (result.Value is not null)
        {
            resourceMutator.MutateStorageResource(result.Value);
        }
        return result;
    }
}