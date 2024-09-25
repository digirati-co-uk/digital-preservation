using System.Xml.XPath;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

/// <summary>
/// The full path including the repository prefix
/// </summary>
/// <param name="path"></param>
public class GetResource(string path) : IRequest<Result<PreservedResource?>>
{
    public string Path { get; } = path;
}

public class GetResourceHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<GetResource, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetResource request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.GetResource(request.Path);
        if (result.Value is not null)
        {
            resourceMutator.MutateStorageResource(result.Value);
        }
        return result;
    }
}