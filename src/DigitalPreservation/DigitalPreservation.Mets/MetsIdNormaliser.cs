using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Mets;

/// <summary>
/// Rewrites the IDs of a METS document minted before issue #214 into the form the platform mints
/// now, and follows every reference to them (issue #188 step 3).
/// </summary>
/// <remarks>
/// The rule is narrow on purpose: an ID that is already a legal NCName is left exactly as it is,
/// whoever minted it. That makes the operation idempotent, keeps it away from IDs that are not ours
/// to renumber, and means a document it reports no change for is one that needs no new version.
/// <para>
/// Nothing here reads a path, or asks what an ID means. Each ID is re-encoded as a string by
/// <see cref="MetsIds.Normalise"/>, which is what makes it safe to run against a document whose
/// conventions we only partly recognise, and what keeps the migration inside the rule that IDs are
/// opaque to code.
/// </para>
/// <para>
/// One METS feature is deliberately not covered: <c>BEGIN</c>/<c>END</c> hold an IDREF when the
/// accompanying <c>BETYPE</c> says <c>IDREF</c>. The platform never writes that, and this only ever
/// runs against a document the platform wrote (<c>GetFullMets</c> refuses any other agent), so the
/// case is unreachable here. It is named rather than left implicit because the safety net for it is
/// elsewhere: the migration verifies the preserved document's ID integrity afterwards, so an ID
/// this missed shows up as a failed migration rather than as a quietly invalid document.
/// </para>
/// </remarks>
public static class MetsIdNormaliser
{
    /// <summary>
    /// Normalise every ID in the document, in place, and report what changed. Nothing is written:
    /// the caller decides whether the report is worth persisting.
    /// </summary>
    public static MetsIdNormalisationReport Normalise(DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        var report = new MetsIdNormalisationReport();
        var nodes = MetsGraph.Nodes(mets).ToList();

        var rewrites = PlanRewrites(nodes, report);
        if (rewrites.Count == 0 && !AnyInvalidReference(nodes))
        {
            return report;
        }

        foreach (var node in nodes)
        {
            var id = MetsGraph.GetId(node);
            if (id is not null && rewrites.TryGetValue(id, out var newId))
            {
                MetsGraph.SetId(node, newId);
                report.IdsRewritten++;
            }
            report.ReferencesRewritten += RewriteReferences(node, rewrites, report);
        }

        return report;
    }

    /// <summary>
    /// Every ID that needs to change, old to new, with the collisions that would make the result
    /// invalid ruled out first.
    /// </summary>
    private static Dictionary<string, string> PlanRewrites(
        List<object> nodes, MetsIdNormalisationReport report)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var invalid = new List<string>();
        foreach (var node in nodes)
        {
            var id = MetsGraph.GetId(node);
            if (id is null)
            {
                continue;
            }
            existing.Add(id);
            if (!MetsIds.IsValidId(id))
            {
                invalid.Add(id);
            }
        }

        var rewrites = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in invalid.Distinct(StringComparer.Ordinal))
        {
            var normalised = MetsIds.Normalise(id);
            if (normalised == id)
            {
                // Nothing left to do to it, but it is still not a legal ID - say so rather than
                // writing a document that silently remains invalid.
                report.Warnings.Add($"ID '{id}' could not be made into a valid xs:ID");
                continue;
            }
            rewrites[id] = normalised;
        }

        RemoveColliding(rewrites, existing, report);

        report.Rewrites.AddRange(rewrites
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MetsIdRewrite { From = pair.Key, To = pair.Value }));
        return rewrites;
    }

    /// <summary>
    /// Drop any rewrite whose new ID is already taken, or that two old IDs both want. Encoding is
    /// injective, so two rewritten IDs cannot collide with each other; the real risk is a rewritten
    /// ID landing on one that was already valid and left alone - possible in a document that was
    /// half migrated, or half written by someone else. Dropping the rewrite leaves that ID invalid
    /// and the document unmigrated, which is the outcome a person should see rather than one where
    /// two elements share an ID and every reference to either becomes ambiguous.
    /// </summary>
    private static void RemoveColliding(
        Dictionary<string, string> rewrites, HashSet<string> existing, MetsIdNormalisationReport report)
    {
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
        var doomed = new List<string>();
        foreach (var (from, to) in rewrites)
        {
            if (existing.Contains(to) && to != from)
            {
                report.Warnings.Add(
                    $"ID '{from}' was not rewritten: '{to}' is already used by another element");
                doomed.Add(from);
            }
            else if (!claimed.TryAdd(to, from))
            {
                report.Warnings.Add(
                    $"IDs '{claimed[to]}' and '{from}' both normalise to '{to}'; neither was rewritten");
                doomed.Add(from);
                doomed.Add(claimed[to]);
            }
        }
        foreach (var from in doomed)
        {
            rewrites.Remove(from);
        }
    }

    /// <summary>
    /// Whether any reference in the document is not a legal NCName even though nothing defines it.
    /// A document with no invalid IDs can still carry an invalid reference - the template writes
    /// DMDID onto folder divs whose dmdSec is only created on first metadata write, so
    /// <c>DMD_metadata/ad-hoc</c> can sit there naming nothing - and leaving it would mean the
    /// document still does not conform after a migration that reported success.
    /// </summary>
    private static bool AnyInvalidReference(List<object> nodes)
    {
        foreach (var node in nodes)
        {
            foreach (var (_, value, _) in MetsGraph.SingleIdRefs(node))
            {
                if (!MetsIds.IsValidId(value))
                {
                    return true;
                }
            }
            foreach (var (_, tokens) in MetsGraph.TokenIdRefs(node))
            {
                if (tokens.Any(token => !MetsIds.IsValidId(token)))
                {
                    return true;
                }
            }
            if (XLinkIdRefs(node).Any(reference => !MetsIds.IsValidId(reference.Value)))
            {
                return true;
            }
        }
        return false;
    }

    private static int RewriteReferences(
        object node, IReadOnlyDictionary<string, string> rewrites, MetsIdNormalisationReport report)
    {
        var changed = 0;

        foreach (var (attribute, value, set) in MetsGraph.SingleIdRefs(node).ToList())
        {
            var replacement = RewriteSingle(value, rewrites, attribute, report);
            if (replacement != value)
            {
                set(replacement);
                changed++;
            }
        }

        foreach (var (attribute, tokens) in MetsGraph.TokenIdRefs(node).ToList())
        {
            changed += RewriteTokens(tokens, rewrites, attribute, report);
        }

        foreach (var (attribute, value, set) in XLinkIdRefs(node).ToList())
        {
            var replacement = RewriteSingle(value, rewrites, attribute, report);
            if (replacement != value)
            {
                set(replacement);
                changed++;
            }
        }

        return changed;
    }

    private static string RewriteSingle(
        string value,
        IReadOnlyDictionary<string, string> rewrites,
        string attribute,
        MetsIdNormalisationReport report)
    {
        if (rewrites.TryGetValue(value, out var mapped))
        {
            return mapped;
        }
        if (MetsIds.IsValidId(value))
        {
            return value;
        }
        // Names nothing in this document, but has to become legal all the same.
        report.Warnings.Add($"{attribute} '{value}' names no element; normalised anyway");
        return MetsIds.Normalise(value);
    }

    /// <summary>
    /// Rewrite an IDREFS attribute, which the XmlSerializer has already split on whitespace. A
    /// legacy ID containing spaces arrives as several tokens, so the joined form is tried first -
    /// exactly as <see cref="IdRefs"/> does when resolving one, and for the same reason: no legal
    /// ID contains a space, so a joined match can only be a legacy ID and is the right answer.
    /// </summary>
    private static int RewriteTokens(
        IList<string> tokens,
        IReadOnlyDictionary<string, string> rewrites,
        string attribute,
        MetsIdNormalisationReport report)
    {
        var joined = IdRefs.Joined(tokens);
        if (rewrites.TryGetValue(joined, out var mappedWhole))
        {
            tokens.Clear();
            tokens.Add(mappedWhole);
            return 1;
        }

        // A genuine IDREFS list names one element per token, and every one of those is either a
        // legal ID or an ID this migration is rewriting. If a token is neither, these tokens are
        // the pieces of a single legacy ID that names nothing - rejoining is the only reading that
        // does not leave a fragment of a filename standing on its own as a reference.
        if (tokens.Any(token => !MetsIds.IsValidId(token) && !rewrites.ContainsKey(token)))
        {
            var normalised = MetsIds.Normalise(joined);
            report.Warnings.Add($"{attribute} '{joined}' names no element; normalised anyway");
            tokens.Clear();
            tokens.Add(normalised);
            return 1;
        }

        var changed = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (rewrites.TryGetValue(tokens[i], out var mapped))
            {
                tokens[i] = mapped;
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// The xlink attributes that hold IDs, which the schema types as plain strings so they cannot
    /// be found the way ADMID and FILEID are.
    /// </summary>
    /// <remarks>
    /// Only these two elements. <c>smArcLink/@xlink:from</c> and <c>@xlink:to</c> have the same
    /// names but hold xlink LABELS, which name <c>smLocatorLink</c> elements within the link group
    /// rather than IDs anywhere in the document; rewriting those would break the group while
    /// looking, from the attribute name alone, like exactly the same job.
    /// </remarks>
    private static IEnumerable<(string Attribute, string Value, Action<string> Set)> XLinkIdRefs(object node)
    {
        switch (node)
        {
            case StructLinkTypeSmLink link:
                if (!string.IsNullOrEmpty(link.From))
                {
                    yield return ("smLink/@xlink:from", link.From, value => link.From = value);
                }
                if (!string.IsNullOrEmpty(link.To))
                {
                    yield return ("smLink/@xlink:to", link.To, value => link.To = value);
                }
                break;

            case StructLinkTypeSmLinkGrpSmLocatorLink locator:
                // xlink:href on a locator is a same-document fragment: "#" plus the ID.
                if (locator.Href is { Length: > 1 } href && href[0] == '#')
                {
                    yield return ("smLocatorLink/@xlink:href", href[1..], value => locator.Href = "#" + value);
                }
                break;
        }
    }
}
