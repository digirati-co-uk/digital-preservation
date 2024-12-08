using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class GetResourceType(string path) : IRequest<Result<string?>>
{
    public string Path { get; } = path;
}


public class GetResourceTypeHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetResourceType, Result<string?>>
{
    public async Task<Result<string?>> Handle(GetResourceType request, CancellationToken cancellationToken)
    {
        return await storageApiClient.GetResourceType(request.Path);
    }
}