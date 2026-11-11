using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;

namespace XmlGen.Tests.AccessConditions;

/// <summary>
/// Profiles for the METS writer tests. The same Leeds matrix is exercised cell by cell in
/// DigitalPreservation.Common.Model.Tests; here it is only the input to the writer.
/// </summary>
public static class TestProfiles
{
    /// <summary>The <c>mods:accessCondition/@type</c> the Leeds copyright status is written under.</summary>
    public const string CopyrightStatusType = "copyright status";

    /// <summary>
    /// The Leeds profile as deployed to dev: Protected Access Level x copyright status -> Access
    /// Tier, from the master matrix. <paramref name="wildcardRow"/> adds the '*' row that gives a
    /// restriction value outside the vocabulary tier 5.
    /// </summary>
    public static AccessConditionProfile Leeds(bool wildcardRow = false)
    {
        var profile = new AccessConditionProfile
        {
            RestrictionAxis = new AccessAxis
            {
                Label = "Protected Access Level",
                Values =
                [
                    new AccessRestriction { Value = "PA1", Label = "Level 1" },
                    new AccessRestriction { Value = "PA2", Label = "Level 2" },
                    new AccessRestriction { Value = "PA3", Label = "Level 3" },
                    new AccessRestriction { Value = "PA4", Label = "Level 4" },
                    new AccessRestriction { Value = "PA-unassessed", Label = "Unassessed" }
                ]
            },
            CategoryAxis = new CategoryAxis
            {
                Label = "Copyright status",
                Type = CopyrightStatusType,
                Values =
                [
                    new AccessCategory { Value = "public-domain", Label = "Out of Copyright (Public Domain / CC0)", RightsStatements = ["NoC-US", "NoC-NC", "NoC-OKLR", "NoC-CR", "NKC", "CC0"] },
                    new AccessCategory { Value = "low-risk", Label = "In Copyright: Assessed (Low Risk)", RightsStatements = ["InC", "InC-EDU", "InC-NC", "InC-RUU", "InC-OW-EU", "CC-BY", "CC-BY-SA", "CC-BY-ND", "CC-BY-NC", "CC-BY-NC-SA", "CC-BY-NC-ND"] },
                    new AccessCategory { Value = "medium-risk", Label = "In Copyright: Assessed (Medium Risk)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU"] },
                    new AccessCategory { Value = "high-risk", Label = "In Copyright: Assessed (High Risk)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU"] },
                    new AccessCategory { Value = "unassessed", Label = "In Copyright: Unassessed", RightsStatements = ["CNE", "UND"] },
                    new AccessCategory { Value = "holder-conditions", Label = "In Copyright: Holder Conditions (High Risk)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU"] }
                ]
            },
            RightsAxis = new RightsAxis { Label = "Rights Statement" },
            Derived =
            [
                new DerivedAccessCondition
                {
                    Type = "tier",
                    Label = "Access Tier",
                    Values =
                    [
                        new AccessRestriction { Value = "0", Label = "Tier 0 - Open" },
                        new AccessRestriction { Value = "1", Label = "Tier 1 - Open" },
                        new AccessRestriction { Value = "2", Label = "Tier 2 - Restricted" },
                        new AccessRestriction { Value = "3", Label = "Tier 3 - Protected" },
                        new AccessRestriction { Value = "4", Label = "Tier 4 - Closed" },
                        new AccessRestriction { Value = "5", Label = "Tier 5 - Unassessed" }
                    ],
                    Matrix = new Dictionary<string, Dictionary<string, string>>
                    {
                        ["PA1"] = new()
                        {
                            ["public-domain"] = "0", ["low-risk"] = "1", ["medium-risk"] = "2",
                            ["high-risk"] = "2", ["unassessed"] = "2", ["holder-conditions"] = "3", ["__none__"] = "2", ["*"] = "3"
                        },
                        ["PA2"] = new() { ["*"] = "3" },
                        ["PA3"] = new() { ["*"] = "3" },
                        ["PA4"] = new() { ["*"] = "4" },
                        ["PA-unassessed"] = new() { ["*"] = "5" },
                        ["__none__"] = new() { ["*"] = "5" }
                    }
                }
            ]
        };
        if (wildcardRow) profile.Derived[0].Matrix["*"] = new Dictionary<string, string> { ["*"] = "5" };
        return profile;
    }
}
