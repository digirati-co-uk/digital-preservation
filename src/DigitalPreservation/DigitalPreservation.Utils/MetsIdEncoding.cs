using System.Xml;

namespace DigitalPreservation.Utils;

/// <summary>
/// Encoding of deposit-relative paths for use inside METS <c>xs:ID</c> attributes (issue #188).
/// An <c>xs:ID</c> must be an XML NCName, which excludes the <c>/</c> and space that appear in
/// almost every path we mint an ID from, so the path component of every ID the platform mints
/// passes through <see cref="ToMetsId"/> first.
/// </summary>
/// <remarks>
/// The encoding is <see cref="XmlConvert.EncodeLocalName"/>: invalid characters become
/// <c>_xNNNN_</c> escapes, a leading digit is escaped the same way, Unicode letters pass
/// through, and a literal <c>_x</c> sequence is self-escaped so the transform is bijective
/// (<see cref="DecodeMetsId"/> is its exact inverse). No consumer decodes METS IDs — they are
/// opaque strings everywhere outside this class — but the inverse exists so the encoding can be
/// shown to lose nothing.
/// </remarks>
public static class MetsIdEncoding
{
    /// <summary>
    /// Encode a deposit-relative path as the variable part of a METS ID. Prefixes
    /// (<c>PHYS_</c>, <c>FILE_</c>, …) are NOT added — callers concatenate their own, and every
    /// prefix is itself a valid NCName start, so prefix + encoded path is always a valid ID.
    /// </summary>
    public static string ToMetsId(this string localPath) => XmlConvert.EncodeLocalName(localPath);

    /// <summary>
    /// Recover the original path from a value produced by <see cref="ToMetsId"/>. The prefix, if
    /// any, must be removed first.
    /// </summary>
    public static string DecodeMetsId(this string id) => XmlConvert.DecodeName(id);
}
