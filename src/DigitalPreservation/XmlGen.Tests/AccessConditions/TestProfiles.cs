using DigitalPreservation.Common.Model.AccessConditions;

namespace XmlGen.Tests.AccessConditions;

/// <summary>
/// Profiles for the METS writer tests. The Leeds matrix is exercised cell by cell in
/// DigitalPreservation.Common.Model.Tests; here it is only the input to the writer.
/// </summary>
public static class TestProfiles
{
    /// <summary>The <c>mods:accessCondition/@type</c> the Leeds copyright statement is written under.</summary>
    public const string CopyrightStatementType = "copyright statement";

    // Statement keys from the Leeds document, by the copyright status column they fall into.
    public const string OutOfCopyright = "out-of-copyright";        // PA1: tier 0
    public const string LowRisk = "inc-low-risk";                   // PA1: tier 1
    public const string HolderConditions = "inc-holder-preservation"; // PA1: tier 3

    /// <summary>
    /// The Leeds profile as deployed (Preservation.API/AccessConditionProfiles/leeds.json).
    /// <paramref name="wildcardRow"/> keeps its '*' row, which gives a restriction value outside
    /// the vocabulary tier 5; without it such a value resolves to nothing.
    /// </summary>
    public static AccessConditionProfile Leeds(bool wildcardRow = false)
    {
        var profile = AccessConditionProfile.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AccessConditionProfiles", "leeds.json")));
        if (!wildcardRow) profile.Statements!.Derived[0].Matrix.Remove(StatementProfile.Wildcard);
        return profile;
    }
}
