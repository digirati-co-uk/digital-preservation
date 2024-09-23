using DigitalPreservation.Common.Model;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

/// <summary>
/// The full path including the repository prefix
/// </summary>
/// <param name="path"></param>
public class GetResource(string path) : IRequest<PreservedResource?>
{
    public string Path { get; } = path;
}

public class GetResourceHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetResource, PreservedResource?>
{
    public async Task<PreservedResource?> Handle(GetResource request, CancellationToken cancellationToken)
    {
        return await storageApiClient.GetResource(request.Path);
    }
}