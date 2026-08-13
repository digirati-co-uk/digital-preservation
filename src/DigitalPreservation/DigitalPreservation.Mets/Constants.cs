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
    public const string DirectoryType = "Directory";
    public const string ItemType = "Item";
    public const string VirusProvEventPrefix = "digiprovMD_ClamAV_";
    public const string NullRightsStatement = "null";

    public const string Physical = "PHYSICAL";
    public const string Logical = "LOGICAL";
}