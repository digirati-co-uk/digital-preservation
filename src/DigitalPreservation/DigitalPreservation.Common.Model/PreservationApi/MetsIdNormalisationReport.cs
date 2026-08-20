using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PreservationApi;

/// <summary>
/// What normalising a METS document's IDs did, or would do (issue #188 step 3). Returned by the
/// deposit normalise operation so a caller can decide whether there is anything worth preserving:
/// a report with <see cref="Changed"/> false means the document already conforms, and preserving
/// it would bump the Archival Group's version for no reason.
/// </summary>
public class MetsIdNormalisationReport
{
    /// <summary>
    /// How many xs:ID attributes were rewritten. Zero means every ID in the document was already
    /// a legal NCName, whenever it was minted.
    /// </summary>
    [JsonPropertyName("idsRewritten")]
    public int IdsRewritten { get; set; }

    /// <summary>
    /// How many IDREF/IDREFS attribute values were rewritten to follow their targets.
    /// </summary>
    [JsonPropertyName("referencesRewritten")]
    public int ReferencesRewritten { get; set; }

    /// <summary>
    /// Every ID that changed, old to new. Kept in full rather than counted: this is the record of
    /// what a preserved document's identifiers used to be, and the only place it exists.
    /// </summary>
    [JsonPropertyName("rewrites")]
    public List<MetsIdRewrite> Rewrites { get; set; } = [];

    /// <summary>
    /// Things a person should look at. Never a reason to fail on its own - the commonest entry is
    /// a reference that names nothing (the template writes DMDID onto folder divs before their
    /// dmdSec exists, so a dangling DMDID is normal) - but a document that produces a lot of these
    /// is not one to migrate unattended.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// IDs carried by more than one element. Unlike <see cref="Warnings"/> this does stop the
    /// operation: a rewrite maps an ID value rather than an element, so both would land on the same
    /// new ID and a document that is visibly invalid would become one that is invalid but looks
    /// fine. Nothing is rewritten when this is non-empty, and the document is left as it was for a
    /// person to sort out.
    /// </summary>
    [JsonPropertyName("duplicateIds")]
    public List<string> DuplicateIds { get; set; } = [];

    /// <summary>
    /// Whether the document was actually modified. A normalisation that changed nothing must not
    /// be preserved.
    /// </summary>
    [JsonPropertyName("changed")]
    public bool Changed => IdsRewritten > 0 || ReferencesRewritten > 0;
}

/// <summary>One ID, before and after.</summary>
public class MetsIdRewrite
{
    [JsonPropertyName("from")]
    public required string From { get; set; }

    [JsonPropertyName("to")]
    public required string To { get; set; }
}
