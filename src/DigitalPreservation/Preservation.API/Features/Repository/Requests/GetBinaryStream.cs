using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class GetBinaryStream(string path, string? version = null) : IRequest<Result<Stream>>
{
    public string Path { get; } = path;
    public string? Version { get; } = version;
}

public class GetBinaryStreamHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetBinaryStream, Result<Stream>>
{
    public async Task<Result<Stream>> Handle(GetBinaryStream request, CancellationToken cancellationToken)
    {
        return await storageApiClient.GetBinaryStream(request.Path, request.Version);
    }
}