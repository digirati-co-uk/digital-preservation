using System.Text.Json;
using DigitalPreservation.Common.Model.AccessConditions;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.AccessConditions;

/// <summary>
/// The body of POST /deposits/{id}/mets/access: a property left out is left alone, one sent as
/// null is cleared, and every value is checked against the installation's profile.
/// </summary>
public class AccessChangeTests
{
    private const string InCopyright = "http://rightsstatements.org/vocab/InC/1.0/";

    private static readonly AccessConditionProfile Standard = AccessConditionProfile.Simple(
    [
        new AccessRestriction { Value = "Open", Label = "Open" },
        new AccessRestriction { Value = "Closed", Label = "Closed" }
    ]);

    private static readonly AccessConditionProfile WithStatements = new()
    {
        RestrictionAxis = Standard.RestrictionAxis,
        Statements = new StatementProfile
        {
            Type = "copyright status",
            Values = [new AccessStatement { Value = "in-copyright-holder-conditions", Label = "In copyright" }]
        }
    };

    [Fact]
    public void A_Property_Left_Out_Was_Not_Sent_And_One_Sent_As_Null_Was()
    {
        var change = Read("""{ "path": "objects/thesis.pdf", "accessRestrictions": null }""");

        change.AccessRestrictionsSent.Should().BeTrue();
        change.AccessRestrictions.Should().BeNull();
        change.RightsStatementSent.Should().BeFalse();
        change.AccessStatementSent.Should().BeFalse();
    }

    [Fact]
    public void A_Change_That_Sends_Nothing_To_Set_Is_A_Problem()
    {
        Read("""{ "path": "objects/thesis.pdf" }""").Problems(Standard)
            .Should().ContainSingle().Which.Should().StartWith("Send at least one");
    }

    [Fact]
    public void Known_Values_Are_Accepted()
    {
        Read($$"""{ "path": "objects", "accessRestrictions": ["Closed"], "rightsStatement": "{{InCopyright}}" }""")
            .Problems(Standard).Should().BeEmpty();
        Read("""{ "path": "objects", "accessRestrictions": [], "rightsStatement": null }""")
            .Problems(Standard).Should().BeEmpty("clearing both so they inherit is a valid change");
        Read("""{ "path": "objects", "accessStatement": "in-copyright-holder-conditions" }""")
            .Problems(WithStatements).Should().BeEmpty();
    }

    [Fact]
    public void A_Restriction_Outside_The_Vocabulary_Is_A_Problem_That_Lists_The_Choices()
    {
        Read("""{ "path": "objects", "accessRestrictions": ["Secret"] }""").Problems(Standard)
            .Should().ContainSingle().Which.Should().Be("'Secret' is not an access restriction here. The choices are: Open, Closed.");
    }

    [Fact]
    public void Two_Restrictions_Are_A_Problem_Unless_The_Installation_Allows_Several()
    {
        var change = Read("""{ "path": "objects", "accessRestrictions": ["Open", "Closed"] }""");
        var several = AccessConditionProfile.Simple(Standard.RestrictionAxis.Values, allowMultiple: true);

        change.Problems(Standard).Should().ContainSingle().Which.Should().Be("Only one access restriction is allowed here.");
        change.Problems(several).Should().BeEmpty();
    }

    [Fact]
    public void An_Unknown_Rights_Statement_Is_A_Problem()
    {
        Read("""{ "path": "objects", "rightsStatement": "https://example.org/my-own-licence" }""").Problems(Standard)
            .Should().ContainSingle().Which.Should().Contain("is not a rights statement the platform knows");
    }

    [Fact]
    public void With_A_Statement_Profile_The_Rights_Statement_Cannot_Be_Sent()
    {
        Read($$"""{ "path": "objects", "rightsStatement": "{{InCopyright}}" }""").Problems(WithStatements)
            .Should().ContainSingle().Which.Should().StartWith("The rights statement follows from the access statement");
    }

    [Fact]
    public void An_Access_Statement_Must_Be_One_The_Installation_Has()
    {
        var change = Read("""{ "path": "objects", "accessStatement": "made-up" }""");

        change.Problems(WithStatements).Should().ContainSingle().Which.Should().StartWith("'made-up' is not an access statement here");
        change.Problems(Standard).Should().ContainSingle().Which.Should().StartWith("This installation has no access statements");
    }

    private static AccessChange Read(string json) => JsonSerializer.Deserialize<AccessChange>(json)!;
}
