using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit.Extensions;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

public class SetModsInformation(
    Uri depositFiles,
    string localPath,
    string depositETag,
    List<string> accessRestrictions,
    Uri? rightsStatement,
    IEnumerable<RecordIdentifier> recordIdentifiers,
    List<FileLink>? fileLinks = null) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;

    public string LocalPath { get; } = localPath;
    public List<string> AccessRestrictions { get; } = accessRestrictions;
    public Uri? RightsStatement { get; } = rightsStatement;

    public RecordInfo RecordInfo { get; } = new(){ RecordIdentifiers = recordIdentifiers.ToList() };
    public string DepositETag { get; } = depositETag;
    public List<FileLink>? FileLinks { get; } = fileLinks;
}

public class SetModsInformationHandler(IMetsManager metsManager) : IRequestHandler<SetModsInformation, Result>
{
    public async Task<Result> Handle(SetModsInformation request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is { Success: true, Value: not null })
        {
            var fullMets = metsResult.Value;
            metsManager.SetAccessRestrictionsByPath(fullMets, request.LocalPath, request.AccessRestrictions);
            metsManager.SetRightsStatementByPath(fullMets, request.LocalPath, request.RightsStatement);
            metsManager.SetRecordInfoByPath(fullMets, request.LocalPath, request.RecordInfo);
            if (request.FileLinks != null)
                metsManager.SetFileLinks(fullMets, request.LocalPath, request.FileLinks);
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