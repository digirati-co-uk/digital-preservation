using DigitalPreservation.Mets;
using DigitalPreservation.Utils;

namespace XmlGen.Tests;

/// <summary>
/// The METS IDs the platform mints for a deposit-relative path (issue #188). Tests build their
/// expectations through here rather than writing encoded literals, so that a change to the
/// encoding surfaces as a deliberate change to <see cref="MetsIdEncoding"/> rather than as a
/// wall of edited strings — and so that a test asserting a RAW id is visibly doing something
/// different (the frozen legacy fixtures in Samples/ still do, deliberately).
/// </summary>
public static class MetsIds
{
    public static string Phys(string localPath) => Constants.PhysIdPrefix + localPath.ToMetsId();
    public static string File(string localPath) => Constants.FileIdPrefix + localPath.ToMetsId();
    public static string Adm(string localPath) => Constants.AdmIdPrefix + localPath.ToMetsId();
    public static string Tech(string localPath) => Constants.TechIdPrefix + localPath.ToMetsId();
    public static string Dmd(string localPath) => Constants.DmdIdPrefix + localPath.ToMetsId();
}
