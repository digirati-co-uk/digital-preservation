using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit.Extensions;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

/// <summary>Set what the metadata dialogue edits on one node of a deposit's METS, and write it.</summary>
public class SetModsInformation(Uri depositFiles, string localPath, string depositETag, ModsChanges changes) : IRequest<Result>
{
    public Uri DepositFiles { get; } = depositFiles;
    public string LocalPath { get; } = localPath;
    public string DepositETag { get; } = depositETag;
    public ModsChanges Changes { get; } = changes;
}

/// <summary>What the metadata dialogue sets on one node.</summary>
/// <param name="AccessRestrictions">
/// The node's own restrictions; empty clears them so it inherits; null leaves them as they are.
/// </param>
/// <param name="Rights">What to do with the node's rights statement.</param>
/// <param name="AccessStatement">
/// The chosen statement on an installation with a statement profile (issue #433); null clears the
/// node's own so it inherits. Without a statement profile only null is accepted.
/// </param>
/// <param name="RecordIdentifiers">The node's record identifiers; null leaves them as they are.</param>
/// <param name="FileLinks">A file's links to other files; null leaves them as they are.</param>
/// <param name="StatementInForm">
/// False when the form had no statement dropdown (it was rendered without the statement profile):
/// the node's statement, and the rights written from it, are then left as they are.
/// </param>
public record ModsChanges(
    List<string>? AccessRestrictions,
    RightsChoice Rights,
    string? AccessStatement,
    IEnumerable<RecordIdentifier>? RecordIdentifiers,
    List<FileLink>? FileLinks,
    bool StatementInForm = true);

/// <summary>The four things a form can say about a node's rights statement.</summary>
public enum RightsAction
{
    /// <summary>Nothing: the form had no rights choice (an installation with a statement profile).</summary>
    LeaveUnchanged,
    /// <summary>Clear the node's own rights statement, so it inherits its parent's.</summary>
    Inherit,
    /// <summary>Record explicitly that no rights statement is chosen here, and stop inheriting.</summary>
    NotChosen,
    /// <summary>Set the node's rights statement.</summary>
    Set
}

public sealed record RightsChoice(RightsAction Action, Uri? Statement = null)
{
    public static readonly RightsChoice LeaveUnchanged = new(RightsAction.LeaveUnchanged);
    public static readonly RightsChoice Inherit = new(RightsAction.Inherit);
    public static readonly RightsChoice NotChosen = new(RightsAction.NotChosen);
    public static RightsChoice SetTo(Uri statement) => new(RightsAction.Set, statement);
}

public class SetModsInformationHandler(IMetsManager metsManager) : IRequestHandler<SetModsInformation, Result>
{
    public async Task<Result> Handle(SetModsInformation request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is not { Success: true, Value: not null })
        {
            return Result.Fail(metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
        }
        var fullMets = metsResult.Value;

        var applied = await ApplyChanges(fullMets, request.LocalPath, request.Changes);
        if (applied.Failure) return applied;

        var written = await metsManager.WriteMets(fullMets);
        return written.Failure
            ? Result.Fail(written.ErrorCode!, written.ErrorMessage ?? "Unable to write METS file.")
            : Result.Ok();
    }

    private async Task<Result> ApplyChanges(FullMets mets, string path, ModsChanges changes)
    {
        var statementType = await metsManager.GetAccessStatementTypeAsync();
        if (statementType.Failure) return statementType;
        Func<Result>[] steps =
        [
            () => changes.AccessRestrictions is null
                ? Result.Ok()
                : metsManager.SetAccessRestrictionsByPath(mets, path, changes.AccessRestrictions),
            () => changes.StatementInForm
                ? metsManager.SetAccessStatementByPath(mets, path, statementType.Value, changes.AccessStatement)
                : Result.Ok(),
            () => SetRights(mets, path, changes.Rights),
            () => changes.RecordIdentifiers is null
                ? Result.Ok()
                : metsManager.SetRecordInfoByPath(mets, path,
                    new RecordInfo { RecordIdentifiers = changes.RecordIdentifiers.ToList() })
        ];
        foreach (var step in steps)
        {
            var result = step();
            if (result.Failure) return result;
        }
        if (changes.FileLinks != null)
        {
            metsManager.SetFileLinks(mets, path, changes.FileLinks);
        }
        return Result.Ok();
    }

    private Result SetRights(FullMets mets, string path, RightsChoice rights) => rights.Action switch
    {
        RightsAction.LeaveUnchanged => Result.Ok(),
        RightsAction.NotChosen => metsManager.SuppressRightsInheritanceByPath(mets, path),
        RightsAction.Inherit => metsManager.SetRightsStatementByPath(mets, path, null),
        _ => metsManager.SetRightsStatementByPath(mets, path, rights.Statement)
    };
}
