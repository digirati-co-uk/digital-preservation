using DigitalPreservation.Utils;

namespace DigitalPreservation.Mets;

/// <summary>
/// The METS IDs the platform mints for a deposit-relative path.
/// </summary>
/// <remarks>
/// One authority, so that every part of the system spells an ID the same way and a change to the
/// scheme is a change to one file. Before this existed the prefix and the encoding were combined
/// by hand at a dozen sites, and the tests had a tidier abstraction than the production code did.
/// <para>
/// An ID carries the path deliberately - it is legible to a person reading the METS, and unique
/// without any allocation, because a deposit-relative path is already unique in a document. But it
/// is opaque to code: nothing may read a path back out of one. See 02d, "Opaque to code, legible
/// to people".
/// </para>
/// </remarks>
public static class MetsIds
{
    public static string Phys(string localPath) => Constants.PhysIdPrefix + localPath.ToMetsId();
    public static string File(string localPath) => Constants.FileIdPrefix + localPath.ToMetsId();
    public static string Adm(string localPath) => Constants.AdmIdPrefix + localPath.ToMetsId();
    public static string Tech(string localPath) => Constants.TechIdPrefix + localPath.ToMetsId();
    public static string Dmd(string localPath) => Constants.DmdIdPrefix + localPath.ToMetsId();

    /// <summary>
    /// A dmdSec ID for a div with no deposit-relative path — a logical div, where the div's own ID
    /// is the only stable stem there is. The same scheme as <see cref="Dmd"/>, named separately so
    /// the call site says which stem it is using; sharing the implementation is the point, so that
    /// a change to how a dmdSec ID is spelt stays one edit here.
    /// </summary>
    public static string DmdFromDivId(string divId) => Dmd(divId);

    /// <summary>
    /// The digiprovMD ID for a file's FIRST virus scan. Later scans of the same file are numbered
    /// by <see cref="Constants.NumberedVirusProvEventId"/>, which takes the identifier this is
    /// built from rather than taking this ID apart again.
    /// </summary>
    public static string VirusProvEvent(string identifier) => Constants.VirusProvEventPrefix + identifier;
}
