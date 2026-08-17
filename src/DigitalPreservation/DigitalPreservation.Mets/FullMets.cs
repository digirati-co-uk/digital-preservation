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

    /// <summary>
    /// Diagnostics from the last <see cref="MetsCache.Populate"/>: one entry per div whose path
    /// could not be resolved or that collided with another div's path. Empty for a well-formed
    /// managed METS. Used to explain navigation failures, and the seed of a future editability
    /// conformance check.
    /// </summary>
    public List<string> PathDiagnostics { get; } = new();

}
