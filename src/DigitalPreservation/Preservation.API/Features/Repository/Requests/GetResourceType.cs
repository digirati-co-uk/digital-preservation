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
        // IStorageApiClient.GetResourceType (CommonApiBase) documents that Path must be the full
        // /repository/... path, which it HEADs as-is against the Storage API - so this handler
        // must not reshape it. RepositoryController.TryStreamConventionalMets already sends the
        // full Request.Path here; it is RepositoryController.HeadResource that was sending only
        // the stripped route segment and is fixed at that call site instead (issue #348), rather
        // than here, which would double the prefix for the caller that already gets this right.
        return await storageApiClient.GetResourceType(request.Path);
    }
}