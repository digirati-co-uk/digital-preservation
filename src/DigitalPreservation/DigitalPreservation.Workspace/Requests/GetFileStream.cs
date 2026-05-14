using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace.Requests;

public class GetFileStream(Uri fileUri) : IRequest<Result<Stream?>>
{
    public Uri FileUri { get; } = fileUri;
}

public class GetFileStreamHandler(IStorage storage) : IRequestHandler<GetFileStream, Result<Stream?>>
{
    public Task<Result<Stream?>> Handle(GetFileStream request, CancellationToken cancellationToken)
        => storage.GetStream(request.FileUri);
}
