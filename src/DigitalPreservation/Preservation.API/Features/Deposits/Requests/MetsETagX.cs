using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Mets;

namespace Preservation.API.Features.Deposits.Requests;

public static class MetsETagX
{
    /// <summary>
    /// Reads the deposit's current METS ETag and sets it, or logs and leaves it null on a failed
    /// read (issue #264) - for use after an operation (create, patch) that has already succeeded,
    /// where the ETag is a courtesy to the caller and must never turn a successful write into a
    /// reported failure. A missing METS (template=None, or an export whose METS hasn't arrived
    /// yet) is not a failure - GetMetsFileWrapper still succeeds, with a null ETag - so this only
    /// logs for a genuine read error.
    /// </summary>
    /// <remarks>
    /// GetDepositHandler's own read is deliberately stricter (a failed read fails the whole
    /// request) and does not use this - GET is expected to be reliable in a way a courtesy field
    /// on an already-successful write is not.
    /// </remarks>
    public static async Task SetMetsETagBestEffort(this IMetsParser metsParser, Deposit deposit, ILogger logger)
    {
        if (deposit.Files is null)
        {
            return;
        }
        var wrapperResult = await metsParser.GetMetsFileWrapper(deposit.Files, false);
        if (wrapperResult.Success)
        {
            deposit.MetsETag = wrapperResult.Value?.ETag;
        }
        else
        {
            logger.LogWarning("Could not read METS ETag for deposit {DepositId}: {CodeAndMessage}",
                deposit.Id, wrapperResult.CodeAndMessage());
        }
    }
}
