namespace DigitalPreservation.Mets;

/// <summary>
/// Bound from the <c>FeatureFlags</c> section, alongside the other flags of whichever service is
/// writing METS.
/// </summary>
public class MetsManagerOptions
{
    /// <summary>
    /// Normalise a document's IDs whenever it is written, so that editing a METS created before
    /// issue #214 migrates it as a side effect (issue #188 step 3).
    /// </summary>
    /// <remarks>
    /// Without this, an edit to a pre-#214 document makes it WORSE, not better: the new entries get
    /// encoded IDs while the existing ones keep their raw form, so the only thing that creates a
    /// mixed-generation document is us editing one. With it, the corpus converges by attrition from
    /// the top while the bulk migration works up from the bottom.
    /// <para>
    /// A flag rather than a permanent behaviour because the first real runs of the normaliser should
    /// happen in a controlled batch, not while somebody waits for a page to save. Turn it on after
    /// the bulk campaign has been through the same documents.
    /// </para>
    /// </remarks>
    public bool NormaliseMetsIdsOnWrite { get; set; }
}
