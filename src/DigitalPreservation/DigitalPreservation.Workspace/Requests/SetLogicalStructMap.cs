using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

public class SetLogicalStructMap(Uri depositFiles, string depositETag, LogicalRange logicalRange) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;
    public string DepositETag { get; } = depositETag;
    public LogicalRange LogicalRange { get; } = logicalRange;
}

public class SetLogicalStructMapHandler(IMetsManager metsManager) : IRequestHandler<SetLogicalStructMap, Result>
{
    public async Task<Result> Handle(SetLogicalStructMap request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is { Success: true, Value: not null })
        {
            var fullMets = metsResult.Value;
            metsManager.SetStructMap(fullMets, request.LogicalRange);
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
