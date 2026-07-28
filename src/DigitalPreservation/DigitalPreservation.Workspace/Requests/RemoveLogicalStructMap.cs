using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

public class RemoveLogicalStructMap(Uri depositFiles, string depositETag, string id) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;
    public string DepositETag { get; } = depositETag;
    public string Id { get; } = id;
}

public class RemoveLogicalStructMapHandler(IMetsManager metsManager) : IRequestHandler<RemoveLogicalStructMap, Result>
{
    public async Task<Result> Handle(RemoveLogicalStructMap request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is { Success: true, Value: not null })
        {
            var fullMets = metsResult.Value;
            metsManager.RemoveStructMap(fullMets, request.Id);
            var writeMetsResult = await metsManager.WriteMets(fullMets);
            if (writeMetsResult.Failure)
            {
                return Result.Fail(writeMetsResult.ErrorCode!, "Unable to write METS file.");
            }
            return Result.Ok();
        }
        return Result.Fail(metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
    }
}
