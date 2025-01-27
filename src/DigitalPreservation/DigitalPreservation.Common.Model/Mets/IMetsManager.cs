using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsManager
{
    public const string MetsCreatorAgent = "University of Leeds Digital Library Infrastructure Project";
    // Create an empty METS file
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit);
    bool IsMetsFile(string fileName);
    
    // Reverse-engineer a METS file from an existing AG. This is OK for now but likely to be an error scenario
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit);
    Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile);
    Task<Result> HandleDeleteObject(Uri workingRoot, string localPath);
    Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory);
}