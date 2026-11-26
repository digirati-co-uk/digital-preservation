using DigitalPreservation.Common.Model;
using System.Net;
using Amazon.S3;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Storage.API.Fedora;
using Storage.API.Fedora.Vocab;

namespace Storage.API.Features.Import;

public enum CommitVerification
{
    /// <summary>S3 shows the new version, and Fedora agrees.</summary>
    Confirmed,

    /// <summary>S3 was read and the version never moved: the commit didn't happen.</summary>
    ConfirmedNotCommitted,

    /// <summary>No trustworthy read was ever made.</summary>
    CouldNotConfirm,

    /// <summary>
    /// S3 shows the new version but Fedora doesn't (issue #459): Fedora died between OCFL committing and
    /// its own database committing. The version exists, but the group needs repair before its next import.
    /// </summary>
    FedoraDisagrees
}

/// <param name="Outcome">What the check concluded.</param>
/// <param name="NewVersion">The head version S3 reported, once a read succeeded.</param>
/// <param name="Message">For <see cref="CommitVerification.FedoraDisagrees"/>, what disagreed.</param>
public record CommitVerificationResult(CommitVerification Outcome, string? NewVersion = null, string? Message = null);

/// <summary>
/// Decides whether a commit whose own response can't be trusted actually happened. Used by
/// <see cref="Requests.ExecuteImportJobHandler"/> after a connection error, a gateway status or a
/// cancelled commit PUT (issue #383), and by <see cref="ImportJobCommitReconciler"/> after the importer
/// died mid-commit (issue #444). Every caller must already have waited for the transaction to finish.
///
/// It asks S3 first, as #383 did: the OCFL root inventory's head version. When that shows the new
/// version, it then asks Fedora too (issue #459): S3 can show a version that Fedora's database doesn't,
/// if Fedora died after writing the root inventory and before its own commit. Fedora must list the same
/// number of versions, and one resource the job added must be there, or one it deleted gone.
/// </summary>
public class CommitOutcomeVerifier(
    IFedoraClient fedoraClient,
    ILogger logger,
    Func<TimeSpan, CancellationToken, Task>? delayOverride = null)
{
    /// <summary>
    /// Exponential backoff between attempts (2s, 4s, 8s, 16s - about 30s in all) gives Fedora time to
    /// finish a commit that was still running when the PUT's own response became untrustworthy.
    /// </summary>
    public const int MaxAttempts = 5;

    public const string RunbookPointer =
        "The Archival Group needs checking: see docs/internals/wedged-archival-group.md, state 3 (#447)";

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delayOverride ?? Task.Delay;

    /// <summary>
    /// Polls with backoff. A cheap version check each time (issue #244: a full read of a wide group can
    /// take 10-30 minutes), never anything read before the commit. A new group existing at all, with a
    /// version, is committed; an update needs its version to have moved past <paramref name="sourceVersion"/>.
    ///
    /// Any exception from a read is an unconfirmed attempt. The one exception: for a create, S3 saying
    /// the group doesn't exist is evidence the create never committed, because the transaction has
    /// already finished, so there is nothing left for a retry to wait out (issue #383).
    ///
    /// CancellationToken.None for the delays: by now work may be committed to Fedora, and a caller
    /// shutting down must not cut the check short.
    /// </summary>
    public async Task<CommitVerificationResult> Verify(
        string archivalGroupPathUnderRoot, bool isUpdate, string? sourceVersion, ImportJobResult changes)
    {
        var everReadSuccessfully = false;
        string? lastSeenVersion = null;
        string? lastDisagreement = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var versionResult = await fedoraClient.GetArchivalGroupVersion(archivalGroupPathUnderRoot);
                if (versionResult is { Success: true, Value: not null })
                {
                    everReadSuccessfully = true;
                    var newVersion = versionResult.Value.OcflVersion;
                    lastSeenVersion = newVersion;
                    var committed = isUpdate
                        ? newVersion.HasText() && newVersion != sourceVersion
                        : newVersion.HasText();
                    if (committed)
                    {
                        lastDisagreement = await FedoraDisagreement(archivalGroupPathUnderRoot, newVersion!, changes);
                        if (lastDisagreement == null)
                        {
                            return new CommitVerificationResult(CommitVerification.Confirmed, newVersion);
                        }
                        logger.LogWarning(
                            "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath}: S3 shows {Version} but {Disagreement}",
                            attempt, MaxAttempts, archivalGroupPathUnderRoot, newVersion, lastDisagreement);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath}: {CodeAndMessage}",
                        attempt, MaxAttempts, archivalGroupPathUnderRoot, versionResult.CodeAndMessage());
                }
            }
            catch (AmazonS3Exception ex) when (!isUpdate && ex.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation(ex,
                    "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath}: the Archival Group still doesn't exist in S3, and the transaction has already finished, so the create never committed",
                    attempt, MaxAttempts, archivalGroupPathUnderRoot);
                return new CommitVerificationResult(CommitVerification.ConfirmedNotCommitted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath} threw",
                    attempt, MaxAttempts, archivalGroupPathUnderRoot);
            }

            if (attempt < MaxAttempts)
            {
                await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), CancellationToken.None);
            }
        }

        if (lastDisagreement != null)
        {
            return new CommitVerificationResult(CommitVerification.FedoraDisagrees, lastSeenVersion,
                $"S3 shows version {lastSeenVersion} of {archivalGroupPathUnderRoot}, but {lastDisagreement}. {RunbookPointer}");
        }
        return everReadSuccessfully
            ? new CommitVerificationResult(CommitVerification.ConfirmedNotCommitted, lastSeenVersion)
            : new CommitVerificationResult(CommitVerification.CouldNotConfirm);
    }

    /// <summary>
    /// Null when Fedora agrees with S3's <paramref name="ocflVersion"/>, otherwise what differs. A Fedora
    /// read that fails also counts as a disagreement for this attempt: the caller retries, and only the
    /// last attempt's answer is reported.
    /// </summary>
    private async Task<string?> FedoraDisagreement(string archivalGroupPathUnderRoot, string ocflVersion, ImportJobResult changes)
    {
        if (!TryParseVersionNumber(ocflVersion, out var ocflVersionNumber))
        {
            return $"its version {ocflVersion} can't be compared with Fedora's";
        }

        var fedoraVersions = await fedoraClient.GetFedoraVersionCount(archivalGroupPathUnderRoot);
        if (fedoraVersions.Failure)
        {
            return $"Fedora's versions couldn't be read ({fedoraVersions.CodeAndMessage()})";
        }
        if (fedoraVersions.Value != ocflVersionNumber)
        {
            return $"Fedora lists {fedoraVersions.Value} version(s)";
        }

        // One resource is enough: Fedora's index commits in a single database transaction, so either
        // everything the job did is there or none of it is.
        var added = changes.BinariesAdded.Select(b => b.Id)
            .Concat(changes.ContainersAdded.Select(c => c.Id))
            .FirstOrDefault(id => id != null);
        if (added != null)
        {
            var type = await fedoraClient.GetResourceType(added.GetPathUnderRoot());
            return type.Success && type.Value != RepositoryTypes.Tombstone
                ? null
                : $"{added.GetPathUnderRoot()}, which the import added, isn't in Fedora";
        }

        var deleted = changes.BinariesDeleted.Select(b => b.Id)
            .Concat(changes.ContainersDeleted.Select(c => c.Id))
            .FirstOrDefault(id => id != null);
        if (deleted != null)
        {
            var type = await fedoraClient.GetResourceType(deleted.GetPathUnderRoot());
            var gone = type.ErrorCode == ErrorCodes.NotFound || (type.Success && type.Value == RepositoryTypes.Tombstone);
            return gone ? null : $"{deleted.GetPathUnderRoot()}, which the import deleted, is still in Fedora";
        }

        return null;
    }

    private static bool TryParseVersionNumber(string ocflVersion, out int number)
    {
        number = 0;
        return ocflVersion.Length > 1
               && ocflVersion[0] is 'v' or 'V'
               && int.TryParse(ocflVersion.AsSpan(1), out number);
    }
}
