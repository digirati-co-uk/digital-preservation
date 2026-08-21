using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DigitalPreservation.Workspace.Requests;

/// <summary>
/// Rewrite the deposit's METS IDs into the form the platform mints now (issue #188 step 3), and
/// write it back only if anything changed.
/// </summary>
public class NormaliseMetsIds(Uri depositFiles, string depositETag) : IRequest<Result<MetsIdNormalisationReport>>
{
    public Uri DepositFiles { get; } = depositFiles;
    public string DepositETag { get; } = depositETag;
}

public class NormaliseMetsIdsHandler(IMetsManager metsManager, ILogger<NormaliseMetsIdsHandler> logger)
    : IRequestHandler<NormaliseMetsIds, Result<MetsIdNormalisationReport>>
{
    public async Task<Result<MetsIdNormalisationReport>> Handle(
        NormaliseMetsIds request, CancellationToken cancellationToken)
    {
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<MetsIdNormalisationReport>(
                metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
        }

        var fullMets = metsResult.Value;
        var normaliseResult = metsManager.NormaliseIds(fullMets);
        if (normaliseResult is not { Success: true, Value: not null })
        {
            logger.LogError("Could not normalise METS IDs in {DepositFiles}: {ErrorMessage}",
                request.DepositFiles, normaliseResult.ErrorMessage);
            return normaliseResult;
        }

        var report = normaliseResult.Value;
        if (!report.Changed)
        {
            // Not a failure, and not a write. A document that already conforms must not be
            // rewritten: the deposit's ETag would change, and preserving it afterwards would give
            // the Archival Group a new version identical to the one before it.
            logger.LogInformation("METS in {DepositFiles} already conforms; nothing written",
                request.DepositFiles);
            return Result.OkNotNull(report);
        }

        var writeResult = await metsManager.WriteMets(fullMets);
        if (writeResult.Failure)
        {
            return Result.FailNotNull<MetsIdNormalisationReport>(
                writeResult.ErrorCode ?? ErrorCodes.UnknownError, "Unable to write METS file.");
        }

        logger.LogInformation(
            "Normalised {IdsRewritten} ID(s) and {ReferencesRewritten} reference(s) in {DepositFiles}; " +
            "{WarningCount} warning(s)",
            report.IdsRewritten, report.ReferencesRewritten, request.DepositFiles, report.Warnings.Count);
        foreach (var warning in report.Warnings)
        {
            logger.LogWarning("METS ID normalisation in {DepositFiles}: {Warning}",
                request.DepositFiles, warning);
        }

        return Result.OkNotNull(report);
    }
}
