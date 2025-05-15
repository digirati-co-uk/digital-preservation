using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using Preservation.API.Data;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public static class ArchivalGroupRequestValidator
{
    public static async Task<(bool?, Result<Deposit?>)> ValidateArchivalGroup(
        PreservationContext dbContext,
        IStorageApiClient storageApiClient,
        Deposit deposit, 
        string? mintedId = null,
        bool checkExistence = true)
    {
        if (deposit.ArchivalGroup == null)
        {
            return (false, Result.Ok(deposit));
        }
        var archivalGroupPathUnderRoot = deposit.ArchivalGroup.GetPathUnderRoot(true);
        var validPathResult = PreservedResource.ValidPath(archivalGroupPathUnderRoot);
        if (validPathResult.Failure)
        {
            return (false, Result.Fail<Deposit?>(ErrorCodes.BadRequest, $"Archive path '{archivalGroupPathUnderRoot}' contains invalid characters. {validPathResult.ErrorMessage}"));
        }
        if (checkExistence)
        {
            if (dbContext.Deposits.Any(d => d.Active && d.ArchivalGroupPathUnderRoot == archivalGroupPathUnderRoot && d.MintedId != mintedId))
            {
                return (null, Result.Fail<Deposit?>(ErrorCodes.Conflict,
                    "An Active Deposit already exists for this archivalGroup (" + archivalGroupPathUnderRoot + ")"));
            }
        }
        var agTypeResult = await storageApiClient.GetResourceType(deposit.ArchivalGroup.AbsolutePath);
        if (agTypeResult.Success)
        {
            if (agTypeResult.Value == nameof(ArchivalGroup))
            {
                return (true, Result.Ok<Deposit?>(deposit));
            }

            return (false, Result.Fail<Deposit?>(ErrorCodes.Conflict,
                $"The resource at {archivalGroupPathUnderRoot} is a {agTypeResult.Value}, not an ArchivalGroup"));
        }

        if (agTypeResult.ErrorCode == ErrorCodes.NotFound)
        {
            return (false, Result.Ok<Deposit?>(deposit));
        }

        return (null, Result.Fail<Deposit?>(agTypeResult.ErrorCode!, 
            "Cannot examine Archival Group: " + agTypeResult.ErrorMessage));
    }
}