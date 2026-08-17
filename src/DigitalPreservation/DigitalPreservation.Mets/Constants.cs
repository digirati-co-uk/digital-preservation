using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Mets;

public static class Constants
{
    public const string MetsCreatorAgent = "University of Leeds Digital Library Infrastructure Project";
    public const string RestrictionOnAccess = "restriction on access";
    public const string UseAndReproduction = "use and reproduction";
    public const string Mets = "METS";
    public const string PhysIdPrefix = "PHYS_";
    public const string FileIdPrefix = "FILE_";
    public const string AdmIdPrefix = "ADM_";
    public const string DmdIdPrefix = "DMD_";
    public const string TechIdPrefix = "TECH_";
    public const string DmdPhysRoot = "DMD_PHYS_ROOT";
    public const string ObjectsDivId = PhysIdPrefix + FolderNames.Objects;
    public const string MetadataDivId = PhysIdPrefix + FolderNames.Metadata;
    // Not a const: the path this ID is built from contains a '/', which an xs:ID may not, so it
    // has to go through the same encoding as every other minted ID (issue #188). METS files
    // written before that fix carry the raw "PHYS_metadata/ad-hoc" form and are still navigated
    // by path, so nothing reads this constant to identify an existing div.
    public static readonly string MetadataAdHocDivId = PhysIdPrefix + FolderNames.MetadataAdHoc.ToMetsId();
    // The fileGrp holding the preserved files. Other groups (derivatives, ALTO, thumbnails)
    // appear in third-party METS and may carry an entry with the same href.
    public const string ObjectsFileGrpUse = "OBJECTS";
    public const string DirectoryType = "Directory";
    public const string ItemType = "Item";
    public const string VirusProvEventPrefix = "digiprovMD_ClamAV_";

    /// <summary>
    /// The premis:eventType we write for a virus scan, and the only value by which we recognise one
    /// of our own scan events again. Here rather than on either side for the same reason as
    /// <see cref="NumberedVirusProvEventId"/>: the writer mints it and the writer later reads it
    /// back to tell whether a scan is already recorded, and the two must not drift.
    /// </summary>
    public const string VirusCheckEventType = "virus check";

    /// <summary>
    /// The PREMIS v3 namespace, and the only place in this library it is spelled out. A version
    /// change is a change to what we write and what we recognise, so it should be one edit rather
    /// than a search.
    /// </summary>
    public const string PremisNamespace = "http://www.loc.gov/premis/v3";

    /// <summary>
    /// The ID of the Nth virus-scan event for one file, for N greater than 1 — scans accumulate
    /// as separate events and each needs a unique xs:ID.
    /// </summary>
    /// <remarks>
    /// The counter sits BETWEEN the prefix and the identifier, and that placement is load-bearing.
    /// A trailing counter reads as part of a path: <c>…ClamAV_ADM_objects_x002F_a_2</c> would be
    /// both the second scan of <c>objects/a</c> and the plain ID of a file genuinely named
    /// <c>objects/a_2</c>, so one file's virus status could be reported as another's. A
    /// conventional ID always has the identifier's own prefix straight after the ClamAV prefix,
    /// never a digit, so a numbered ID cannot be mistaken for any file's.
    /// This lives here rather than on either side because the writer mints it and the parser
    /// reads it, and they must not drift — see the MetsParser/MetsManager separation in CLAUDE.md.
    /// </remarks>
    public static string NumberedVirusProvEventId(int occurrence, string identifier) =>
        $"{VirusProvEventPrefix}{occurrence}_{identifier}";
    public const string NullRightsStatement = "null";

    public const string Physical = "PHYSICAL";
    public const string Logical = "LOGICAL";
}