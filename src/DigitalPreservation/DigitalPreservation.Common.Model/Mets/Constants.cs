using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.Mets;

public static class Constants
{
    public const string MetsCreatorAgent = "University of Leeds Digital Library Infrastructure Project";
    public const string RestrictionOnAccess = "restriction on access";
    public const string UseAndReproduction = "use and reproduction";
    public const string Mets = "METS";
    public const string PhysIdPrefix = "PHYS_";
    public const string FileIdPrefix = "FILE_";
    public const string AdmIdPrefix = "ADM_";
    public const string TechIdPrefix = "TECH_";
    public const string DmdPhysRoot = "DMD_PHYS_ROOT";
    public const string ObjectsDivId = PhysIdPrefix + FolderNames.Objects;
    public const string MetadataDivId = PhysIdPrefix + FolderNames.Metadata;
    public const string DirectoryType = "Directory";
    public const string ItemType = "Item";
    public const string VirusProvEventPrefix = "digiprovMD_ClamAV_";
}