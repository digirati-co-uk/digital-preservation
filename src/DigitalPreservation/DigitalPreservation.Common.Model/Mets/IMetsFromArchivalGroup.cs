using DigitalPreservation.Common.Model.Results;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsFromArchivalGroup
{
    // Reverse-engineer a METS file from an existing AG. This is OK for now but likely to be an error scenario
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit);
}