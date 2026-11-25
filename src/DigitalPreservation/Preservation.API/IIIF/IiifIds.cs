using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalPreservation.Utils;

namespace Preservation.API.IIIF;

/// <summary>
/// The identifier a preserved file has in the IIIF projection (issue #261): the canvas id's last
/// segment, and the asset id suggested to a builder that registers the file with IIIF-CS. One
/// value for both, so a builder can line them up without a lookup.
/// </summary>
/// <remarks>
/// <para>
/// Built from the file's full path under <c>/repository</c> (the Archival Group's path, then the
/// file's own), which is already unique across the repository. The steps:
/// </para>
/// <list type="number">
/// <item>Fold diacritics: decompose (NFD) and drop the combining marks, so <c>Fürth</c> becomes
/// <c>Furth</c>.</item>
/// <item>Encode the result the way METS IDs are encoded (<see cref="MetsIdEncoding.ToMetsId"/>), so
/// <c>/</c>, spaces and the like become <c>_xNNNN_</c> escapes.</item>
/// <item>Escape whatever is still not ASCII the same way, so the id is ASCII however the file is
/// named. METS IDs keep non-ASCII letters; this projection does not, because asset ids end up in
/// URLs and other systems' keys.</item>
/// <item>When step 1 changed anything, or the result is longer than <see cref="MaxLength"/>
/// (it is then truncated), append <c>_h</c> and the first 12 hex digits of the SHA-256 of the
/// original path. Folding and truncation lose information; the hash puts back the uniqueness, so
/// <c>résumé.pdf</c> and <c>resume.pdf</c> in the same folder get different ids.</item>
/// </list>
/// <para>
/// The result is a valid XML NCName and contains no <c>/</c>, <c>=</c> or <c>,</c>, so it is also a
/// valid short canvas id for IIIF-CS. It is legible when short, and opaque to code: nothing may read
/// a path back out of one.
/// </para>
/// </remarks>
public static class IiifIds
{
    /// <summary>
    /// The longest id this produces. Protagonist allows 500 characters for an asset id including
    /// the customer and space, and 200 for an adjunct id; this keeps both, and the URLs the id
    /// appears in, comfortable.
    /// </summary>
    public const int MaxLength = 200;

    private const string HashMarker = "_h";
    private const int HashLength = 12;

    public static string ForPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var folded = FoldDiacritics(path);
        var encoded = EscapeNonAscii(folded.ToMetsId());

        var lossy = !string.Equals(folded, path, StringComparison.Ordinal);
        if (!lossy && encoded.Length <= MaxLength)
        {
            return encoded;
        }

        var suffix = HashMarker + HashOf(path);
        var room = MaxLength - suffix.Length;
        return (encoded.Length > room ? encoded[..room] : encoded) + suffix;
    }

    private static string FoldDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // After ToMetsId, which escapes a literal "_x" already, so an escape added here can't be
    // confused with text that was in the path. Same spelling as XmlConvert's: four hex digits, or
    // eight for a character outside the Basic Multilingual Plane.
    private static string EscapeNonAscii(string value)
    {
        if (value.All(char.IsAscii))
        {
            return value;
        }
        var sb = new StringBuilder(value.Length + 16);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.IsAscii)
            {
                sb.Append((char)rune.Value);
            }
            else
            {
                sb.Append(rune.Value > 0xFFFF ? $"_x{rune.Value:X8}_" : $"_x{rune.Value:X4}_");
            }
        }
        return sb.ToString();
    }

    private static string HashOf(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..HashLength];
}
