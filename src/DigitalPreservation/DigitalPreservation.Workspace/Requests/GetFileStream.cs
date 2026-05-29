using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace.Requests;

public class GetFileStream(Uri fileUri) : IRequest<Result<(Stream?, DateTime)>>
{
    public Uri FileUri { get; } = fileUri;
}

public class GetFileStreamHandler(IStorage storage) : IRequestHandler<GetFileStream, Result<(Stream?, DateTime)>>
{
    public Task<Result<(Stream?, DateTime)>> Handle(GetFileStream request, CancellationToken cancellationToken)
        => storage.GetStream(request.FileUri);
}
