using MediatR;

namespace Storage.Repository.Common.Requests;

public class VerifyS3Reachable(string source) : IRequest<ConnectivityCheckResult>
{
    // Who is testing S3 access?
    public string Source { get; set; } = source;
}

public class VerifyS3ReachableHandler(IStorage storage)
    : IRequestHandler<VerifyS3Reachable, ConnectivityCheckResult>
{
    public async Task<ConnectivityCheckResult> Handle(VerifyS3Reachable request, CancellationToken cancellationToken)
    {
        return await storage.CanSeeStorage(request.Source);
    }
}