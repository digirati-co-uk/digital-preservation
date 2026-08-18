namespace DigitalPreservation.Mets;

/// <summary>
/// Resolution helpers for METS IDREFS attributes (ADMID, DMDID) read through the XmlGen typed
/// model, where the XmlSerializer splits the attribute value on whitespace into a token
/// collection. Two incompatible realities have to be served:
/// <list type="bullet">
/// <item>legacy platform METS (pre issue #188) minted IDs containing spaces, so ONE intended
/// ID arrives as SEVERAL tokens and must be rejoined before lookup;</item>
/// <item>schema-valid METS may genuinely reference several elements, one complete ID per
/// token.</item>
/// </list>
/// The joined form is tried first: no schema-valid ID can contain a space, so a joined
/// multi-token string can only match in a legacy document, where it is the right answer.
/// After issue #188 step 2 no newly minted ID can contain a space, so the joined tier only
/// ever fires for legacy content; it is removable after a step 3 bulk migration.
/// </summary>
public static class IdRefs
{
    /// <summary>
    /// The characters XML treats as whitespace, and therefore as IDREFS separators. The
    /// XmlSerializer splits on all of them; the raw-string side must do the same.
    /// </summary>
    private static readonly char[] XmlWhitespace = [' ', '\t', '\n', '\r'];

    /// <summary>The single intended ID that a legacy space-containing ID was split from.</summary>
    public static string Joined(IEnumerable<string> tokens) => string.Join(' ', tokens);

    /// <summary>
    /// Resolve an IDREFS token collection to one referenced element: first as a legacy single
    /// ID containing spaces (the joined form), otherwise each token individually with the
    /// first match winning. Returns null - never throws - when nothing matches or the
    /// collection is empty.
    /// </summary>
    public static T? ResolveSingle<T>(IReadOnlyList<string> tokens, Func<string, T?> lookupById)
        where T : class
    {
        return tokens.Count switch
        {
            0 => null,
            1 => lookupById(tokens[0]),
            _ => lookupById(Joined(tokens))
                 ?? tokens.Select(lookupById).FirstOrDefault(match => match != null)
        };
    }

    /// <summary>
    /// Mirror-image resolution for the XDocument/raw-string side (MetsParser), where the whole
    /// IDREFS attribute value arrives as one string: try the whole value first (a single ID,
    /// or a legacy ID containing spaces), then each whitespace-separated token (schema-valid
    /// IDREFS list - XML allows tab/newline/CR separators as well as spaces), first match
    /// winning. Deliberately NOT implemented by delegating to the token-collection overload:
    /// this side's first tier must match the attribute value VERBATIM (a legacy ID could
    /// contain any whitespace a filename can), whereas the collection overload's joined tier
    /// reconstructs with single spaces because that is all the XmlSerializer's split leaves
    /// it - the two first tiers are subtly different and each is right for its input.
    /// </summary>
    public static T? ResolveSingle<T>(string attributeValue, Func<string, T?> lookupById)
        where T : class
    {
        var whole = lookupById(attributeValue);
        if (whole != null || attributeValue.IndexOfAny(XmlWhitespace) < 0)
        {
            return whole;
        }
        return attributeValue
            .Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries)
            .Select(lookupById)
            .FirstOrDefault(match => match != null);
    }

    /// <summary>
    /// Resolve an IDREFS token collection to every referenced element. The same tiering as
    /// <see cref="ResolveSingle{T}(IReadOnlyList{string},Func{string,T})"/>: a joined-form
    /// match means the tokens are ONE legacy space-containing ID, so exactly that element is
    /// returned; otherwise each token resolves individually (genuine IDREFS list), skipping
    /// tokens that don't resolve. Never throws; empty when nothing matches.
    /// </summary>
    /// <remarks>
    /// This is also what a caller wants when it can only USE some referenced elements - a file's
    /// technical metadata has to come from a section that actually carries PREMIS, not merely one
    /// that exists. Resolve, then select: <c>ResolveAll(...).FirstOrDefault(usable)</c>.
    /// <para>
    /// Selecting afterwards is what preserves the tiering's meaning rather than fighting it. A
    /// joined-form (or single-token) match is an IDENTITY match - it says "this one section is
    /// what the reference names" - so it is the only candidate, and an unusable one yields
    /// nothing rather than sending the caller on to resolve a FRAGMENT of a legacy ID against
    /// some unrelated section. A genuine multi-token list has no meaningful order, so every
    /// candidate is offered and the caller takes the first it can use. An empty result means the
    /// reference dangles; a non-empty result with nothing usable means the sections exist but
    /// hold none of what was wanted - two different diagnostics, distinguishable at the call site.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<T> ResolveAll<T>(IReadOnlyList<string> tokens, Func<string, T?> lookupById)
        where T : class
    {
        switch (tokens.Count)
        {
            case 0:
                return [];
            case 1:
                return lookupById(tokens[0]) is { } single ? [single] : [];
            default:
                if (lookupById(Joined(tokens)) is { } legacy)
                {
                    return [legacy];
                }
                return tokens
                    .Select(lookupById)
                    .Where(match => match != null)
                    .Distinct()
                    .Cast<T>()
                    .ToList();
        }
    }

    /// <summary>
    /// The raw-string counterpart of
    /// <see cref="ResolveAll{T}(IReadOnlyList{string},Func{string,T})"/>, for the
    /// XDocument side where the whole IDREFS attribute value arrives as one string. Same tiering,
    /// and the same reason for not delegating to the collection overload: this side's first tier
    /// must match the attribute value VERBATIM, since a legacy ID could contain any whitespace a
    /// filename can.
    /// </summary>
    public static IReadOnlyList<T> ResolveAll<T>(string attributeValue, Func<string, T?> lookupById)
        where T : class
    {
        if (lookupById(attributeValue) is { } whole)
        {
            return [whole];
        }
        if (attributeValue.IndexOfAny(XmlWhitespace) < 0)
        {
            return [];
        }
        return attributeValue
            .Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries)
            .Select(lookupById)
            .Where(match => match != null)
            .Distinct()
            .Cast<T>()
            .ToList();
    }

    /// <summary>
    /// Remove from an IDREFS token collection the reference to an element previously resolved
    /// from it: every token when the tokens jointly formed a legacy space-containing ID, else
    /// just the token equal to the element's ID. Other references are left intact.
    /// </summary>
    public static void RemoveReference(IList<string> tokens, string resolvedId)
    {
        if (Joined(tokens) == resolvedId)
        {
            tokens.Clear();
        }
        else
        {
            tokens.Remove(resolvedId);
        }
    }
}
