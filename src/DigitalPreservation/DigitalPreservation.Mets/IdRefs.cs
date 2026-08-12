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
    /// Resolve an IDREFS token collection to every referenced element. The same tiering as
    /// <see cref="ResolveSingle{T}(IReadOnlyList{string},Func{string,T})"/>: a joined-form
    /// match means the tokens are ONE legacy space-containing ID, so exactly that element is
    /// returned; otherwise each token resolves individually (genuine IDREFS list), skipping
    /// tokens that don't resolve. Never throws; empty when nothing matches.
    /// </summary>
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
    /// True when the token collection references the element with this ID - as its only token,
    /// as one token of a genuine IDREFS list, or as the joined form of a legacy
    /// space-containing ID. Pure string comparison; no lookup involved.
    /// </summary>
    public static bool References(IReadOnlyList<string> tokens, string id) =>
        tokens.Contains(id) || (tokens.Count > 1 && Joined(tokens) == id);

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

    /// <summary>
    /// Mirror-image resolution for the XDocument/raw-string side (MetsParser), where the whole
    /// IDREFS attribute value arrives as one string: try the whole value first (a single ID,
    /// or a legacy ID containing spaces), then each whitespace-separated token (schema-valid
    /// IDREFS list - XML allows tab/newline/CR separators as well as spaces), first match
    /// winning.
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
}
