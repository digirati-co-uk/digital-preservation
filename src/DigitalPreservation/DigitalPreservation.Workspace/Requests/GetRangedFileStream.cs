using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace.Requests;

public class GetRangedFileStream(Uri fileUri, long from, long? to) : IRequest<Result<RangedStreamResult?>>
{
    public Uri FileUri { get; } = fileUri;
    public long From { get; } = from;
    public long? To { get; } = to;
}

public class GetRangedFileStreamHandler(IStorage storage) : IRequestHandler<GetRangedFileStream, Result<RangedStreamResult?>>
{
    public Task<Result<RangedStreamResult?>> Handle(GetRangedFileStream request, CancellationToken cancellationToken)
        => storage.GetRangedStream(request.FileUri, request.From, request.To);
}
