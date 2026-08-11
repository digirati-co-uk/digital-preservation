using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Mets;

public class FullMets
{
    public required DigitalPreservation.XmlGen.Mets.Mets Mets { get; set; }
    public required Uri Uri { get; set; }
    public string? ETag { get; set; }

    /// <summary>
    /// Maps each deposit-relative path (BagIt data/ prefix stripped) to its div in the PHYSICAL
    /// structMap. Paths are resolved from premis:originalName (directories) and FLocat href
    /// (files), never from div IDs, so navigation does not depend on the ID format (issue #188).
    /// Populated by <see cref="MetsCache.Populate"/> on load and maintained by every MetsManager
    /// mutation. The PHYS_ROOT div is not cached; it is the implicit root of every path.
    /// </summary>
    public Dictionary<string, DivType> PhysicalDivsByPath { get; } = new();
}
