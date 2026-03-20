using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Mets;

public interface IMetsManager
{
    // Create an empty METS file
    Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit);

    Task<(Uri file, DigitalPreservation.XmlGen.Mets.Mets mets)> GetStandardMets(Uri metsLocation, string? agNameFromDeposit);
    
    Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile, string depositETag); // , Uri? storageLocation
    Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag);
    Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag); // , Uri? storageLocation

    Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch);
    // Synchronous modification of FullMETS - does not save to disk!
    Result AddToMets(FullMets fullMets, WorkingBase workingBase); // , Uri? storageLocation
    Result DeleteFromMets(FullMets fullMets, string deletePath); // , Uri? storageLocation
    Task<Result> WriteMets(FullMets fullMets);

    // Extensions
    // We don't need getters because this information will be exposed in either WorkingFile/Dir or Logical ... Ranges
    // These need to be settable on DIVs in Logical Struct Maps as well as on physical paths.
    // Both are strings; it's clearer if they are separate methods.
    // `string localPath` is either a physical path of a file or directory,
    // `string divId` is the "ID" of a mets:Div - usually a logical one but can be a physical one
    // SetAccessRestrictionsByPath(mets, "objects/child-folder", ["Closed"])
    // SetAccessRestrictionsByDivId(mets, "LOG_0003", ["Sundays Only"])
    // SetAccessRestrictionsByDivId(mets, "PHYS_objects/child-folder", ["Closed"])  // equiv to first one
    void SetRecordInfoByPath(FullMets mets, string localPath, RecordInfo recordInfo);
    void SetRecordInfoByDivId(FullMets mets, string divId, RecordInfo recordInfo);
    void SetRightsStatementByPath(FullMets mets, string localPath, Uri? rightsStatement);
    void SetRightsStatementByDivId(FullMets mets, string divId, Uri? rightsStatement);
    void SetAccessRestrictionsByPath(FullMets mets, string localPath, List<string> accessRestrictions);
    void SetAccessRestrictionsByDivId(FullMets mets, string divId, List<string> accessRestrictions);


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