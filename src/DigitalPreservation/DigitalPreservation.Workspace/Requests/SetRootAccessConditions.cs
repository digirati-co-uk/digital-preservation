using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

public class SetRootAccessConditions(
    Uri depositFiles,
    string depositETag,
    List<string> accessRestrictions,
    Uri? rightsStatement) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;
    public List<string> AccessRestrictions { get; } = accessRestrictions;
    public Uri? RightsStatement { get; } = rightsStatement;
    public string DepositETag { get; } = depositETag;
}

public class SetRootAccessConditionsHandler(IMetsManager metsManager) : IRequestHandler<SetRootAccessConditions, Result>
{
    public async Task<Result> Handle(SetRootAccessConditions request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is { Success: true, Value: not null })
        {
            var fullMets = metsResult.Value;
            metsManager.SetRootAccessRestrictions(fullMets, request.AccessRestrictions);
            metsManager.SetRootRightsStatement(fullMets, request.RightsStatement);
            var writeMetsResult = await metsManager.WriteMets(fullMets);
            if (writeMetsResult.Failure)
            {
                return Result.Fail(writeMetsResult.ErrorCode!, $"Unable to write METS file.");
            }
            return Result.Ok();
        }
        return Result.Fail(metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
    }
}