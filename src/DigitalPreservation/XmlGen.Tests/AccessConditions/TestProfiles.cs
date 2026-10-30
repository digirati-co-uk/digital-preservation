using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;

namespace XmlGen.Tests.AccessConditions;

/// <summary>
/// Profiles for the METS writer tests. The same Leeds matrix is exercised cell by cell in
/// DigitalPreservation.Common.Model.Tests; here it is only the input to the writer.
/// </summary>
public static class TestProfiles
{
    /// <summary>The Leeds profile as deployed to dev, minus the wildcard rights group.</summary>
    public static AccessConditionProfile Leeds(bool wildcardGroup = false) => new()
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
        RightsAxis = new RightsAxis
        {
            Label = "Copyright Statement",
            Groups =
            [
                new RightsGroup { Key = "public-domain", Label = "Out of copyright", Members = ["NoC-US", "NKC", "CC0", "CC-BY"] },
                new RightsGroup { Key = "low-risk", Label = "In copyright, low risk", Members = ["InC-EDU", "InC-NC"] },
                new RightsGroup { Key = "restricted", Label = "In copyright (restrictions apply)", Members = ["InC"] },
                new RightsGroup
                {
                    Key = "unassessed", Label = "Unassessed",
                    Members = wildcardGroup ? ["CNE", "__none__", "*"] : ["CNE", "__none__"]
                }
            ]
        },
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
                    ["PA1"] = new() { ["public-domain"] = "0", ["low-risk"] = "1", ["restricted"] = "2", ["unassessed"] = "2" },
                    ["PA2"] = new() { ["*"] = "3" },
                    ["PA3"] = new() { ["*"] = "3" },
                    ["PA4"] = new() { ["*"] = "4" },
                    ["PA-unassessed"] = new() { ["*"] = "5" },
                    ["__none__"] = new() { ["*"] = "5" }
                }
            }
        ]
    };
}
