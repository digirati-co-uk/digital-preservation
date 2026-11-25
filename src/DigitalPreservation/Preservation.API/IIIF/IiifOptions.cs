namespace Preservation.API.IIIF;

/// <summary>
/// Configuration of the IIIF projection (issue #261), section <c>Iiif</c>.
/// </summary>
public class IiifOptions
{
    public const string SectionName = "Iiif";

    /// <summary>
    /// The JSON-LD context document for the platform's IIIF extensions (<c>accessConditions</c>,
    /// <c>formerAssets</c>). Configurable because the docs site that hosts it may move: a JSON
    /// consumer keys on the property names, so moving it changes only what new manifests say.
    /// </summary>
    public string ExtensionContext { get; set; } =
        "https://digirati-co-uk.github.io/digital-preservation-docs/ns/iiif/context.json";

    /// <summary>The namespace the extension terms are defined in; it moves with the context.</summary>
    public string ExtensionNamespace { get; set; } =
        "https://digirati-co-uk.github.io/digital-preservation-docs/ns/iiif#";
}
