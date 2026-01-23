using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsManager
{
    // Create an empty METS file
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit);
    // Reverse-engineer a METS file from an existing AG. This is OK for now but likely to be an error scenario
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit);
    
    Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile, string depositETag); // , Uri? storageLocation
    Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag);
    Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag); // , Uri? storageLocation

    Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch);
    // Synchronous modification of FullMETS - does not save to disk!
    Result AddToMets(FullMets fullMets, WorkingBase workingBase); // , Uri? storageLocation
    Result DeleteFromMets(FullMets fullMets, string deletePath); // , Uri? storageLocation
    Task<Result> WriteMets(FullMets fullMets);
   
    
    
    
        // The following are made obsolete because they are handled by the more general cases below.
    [Obsolete]
    List<string> GetRootAccessRestrictions(FullMets fullMets);
    [Obsolete]
    void SetRootAccessRestrictions(FullMets fullMets, List<string> accessRestrictions);
    [Obsolete]
    void SetRootRightsStatement(FullMets fullMets, Uri? uri);
    [Obsolete]
    Uri? GetRootRightsStatement(FullMets fullMets);

    // Extensions
    // We don't need getters because this information will be exposed in either WorkingFile/Dir or Logical ... Ranges
    // These need to be settable on DIVs in Logical Struct Maps as well as on physical paths.
    // Both are strings; is it safe for the same method to be used for both?
    // ...instead of `string physicalPath` it's `string divID` 
    // `string locator` is either a physical path of a file or directory,
    // or the "ID" of a mets:Div - usually a logical one but can be a physical one
    // SetRecordIdentifier(mets, "objects/child-folder", "EMu", "MS 1234/8")
    // SetRecordIdentifier(mets, "LOG_0003", "EMu", "MS 1234/9")
    // SetRecordIdentifier(mets, "PHYS_objects/child-folder", "EMu", "MS 1234/8")  // equiv to first one
    // always look for both, throw an exception if you find both?
    void SetRecordIdentifier(FullMets mets, string locator, string source, string value);
    void SetRightsStatement(FullMets mets, string locator, Uri? rightsStatement);
    void SetAccessRestrictions(FullMets mets, string locator, List<string> accessRestrictions);


    // There may be more than one so we need to address them better than this, which assumes only one
    // address by ID? There will almost always be only one so we don't want to complicate the UI
    // Sets a logical struct map (never a physical one)
    void SetStructMap(FullMets mets, LogicalRange logSm);
    // This will overwrite a structMap with the same ID, and append one if the ID is different to any currently existing.

    // maybe this rarely used operation, if we need to change the order:
    void SetStructMapOrder(FullMets mets, string[] ids);

    // And we also need to remove a structMap; always require the id even if only one, as a safety measure
    void RemoveStructMap(FullMets mets, string id);

    // from and to are always physical file paths (?)
    void LinkFile(FullMets mets, string from, string to, Uri role);
    void UnLinkFile(FullMets mets, string from, string to, Uri role);
}