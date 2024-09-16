using MediatR;

namespace Storage.Repository.Common.Requests;

public class VerifyS3Reachable : IRequest<bool>
{
}

public class VerifyS3ReachableHandler(IStorage storage)
    : IRequestHandler<VerifyS3Reachable, bool>
{
    public async Task<bool> Handle(VerifyS3Reachable request, CancellationToken cancellationToken)
    {
        return await storage.CanSeeStorage();
    }
}