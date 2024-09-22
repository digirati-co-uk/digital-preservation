using DigitalPreservation.Common.Model;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class GetResource(string path) : IRequest<PreservedResource?>
{
    public string Path { get; set; } = path;
}

public class GetResourceHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetResource, PreservedResource?>
{
    public async Task<PreservedResource?> Handle(GetResource request, CancellationToken cancellationToken)
    {
        return await storageApiClient.GetResource(request.Path);
    }
}