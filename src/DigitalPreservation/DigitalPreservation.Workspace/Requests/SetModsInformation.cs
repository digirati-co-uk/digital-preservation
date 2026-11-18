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
    bool suppressRightsInheritance,
    IEnumerable<RecordIdentifier> recordIdentifiers,
    List<FileLink>? fileLinks = null,
    string? accessStatement = null,
    bool setRights = true) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;

    public string LocalPath { get; } = localPath;
    public List<string> AccessRestrictions { get; } = accessRestrictions;
    public Uri? RightsStatement { get; } = rightsStatement;

    /// <summary>
    /// When true, write an explicit-but-empty rights statement so the resource stops inheriting
    /// rights from its ancestors (effective rights null). When false, <see cref="RightsStatement"/>
    /// is applied directly — a null value clears the explicit rights and lets the parent's rights flow through.
    /// </summary>
    public bool SuppressRightsInheritance { get; } = suppressRightsInheritance;

    /// <summary>
    /// The chosen statement on an installation whose access condition profile has a statement axis
    /// (for Leeds, the specific copyright statement); null clears the node's own value so it inherits. Ignored
    /// when null on an installation without one, refused otherwise (issue #433).
    /// </summary>
    public string? AccessStatement { get; } = accessStatement;

    /// <summary>
    /// False leaves the node's <c>use and reproduction</c> element exactly as it is: on an installation
    /// whose profile has statements there is no rights statement dropdown, so the form says nothing
    /// about rights and must not be read as "clear them" (issue #433).
    /// </summary>
    public bool SetRights { get; } = setRights;

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
            var setResult = metsManager.SetAccessRestrictionsByPath(fullMets, request.LocalPath, request.AccessRestrictions);
            if (setResult.Failure) return setResult;

            var statementType = await metsManager.GetAccessStatementTypeAsync();
            setResult = metsManager.SetAccessStatementByPath(fullMets, request.LocalPath, statementType, request.AccessStatement);
            if (setResult.Failure) return setResult;

            if (request.SetRights)
            {
                setResult = request.SuppressRightsInheritance
                    ? metsManager.SuppressRightsInheritanceByPath(fullMets, request.LocalPath)
                    : metsManager.SetRightsStatementByPath(fullMets, request.LocalPath, request.RightsStatement);
                if (setResult.Failure) return setResult;
            }

            setResult = metsManager.SetRecordInfoByPath(fullMets, request.LocalPath, request.RecordInfo);
            if (setResult.Failure) return setResult;

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
