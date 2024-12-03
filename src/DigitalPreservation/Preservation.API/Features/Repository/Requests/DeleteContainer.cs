using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;


public class DeleteContainer(string path, bool purge) : IRequest<Result>
{
    public string Path { get; } = path;
    public bool Purge { get; } = purge;
}

public class DeleteContainerHandler(IStorageApiClient storageApiClient) : IRequestHandler<DeleteContainer, Result>
{
    public async Task<Result> Handle(DeleteContainer request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.DeleteContainer(request.Path, request.Purge, cancellationToken);
        return result;
    }
}  