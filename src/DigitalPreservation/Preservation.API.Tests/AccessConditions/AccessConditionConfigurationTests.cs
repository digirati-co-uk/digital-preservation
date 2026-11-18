using DigitalPreservation.Common.Model.AccessConditions;
using Microsoft.Extensions.Configuration;
using Preservation.API.Features.AccessConditions;

namespace Preservation.API.Tests.AccessConditions;

/// <summary>
/// The two configuration forms behind <c>GET /access-conditions</c> (issue #433): the standard
/// <c>AccessConditions</c> list, and a profile document named by <c>AccessConditionProfileFile</c>.
/// Never both.
/// </summary>
public class AccessConditionConfigurationTests : IDisposable
{
    private readonly string contentRoot = Directory.CreateTempSubdirectory("acp-").FullName;

    public void Dispose()
    {
        Directory.Delete(contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private AccessConditionProfile Apply(IConfiguration configuration, string? root = null)
    {
        var profile = new AccessConditionProfile();
        ServiceCollectionX.ApplyConfiguration(profile, configuration, root ?? contentRoot);
        return profile;
    }

    private void WriteDocument(string relativePath, string json)
    {
        var path = Path.Combine(contentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void The_standard_list_fills_only_the_restriction_axis()
    {
        var profile = Apply(Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AccessConditions:1:Value", "Closed"), ("AccessConditions:1:Label", "Closed")));

        profile.RestrictionAxis.Values.Select(v => v.Value).Should().Equal("Open", "Closed");
        profile.RestrictionAxis.Label.Should().Be("Access Restriction(s)");
        profile.AllowMultipleRestrictions.Should().BeFalse();
        profile.HasStatements.Should().BeFalse();
        profile.Statements.Should().BeNull();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void AllowMultipleAccessConditions_goes_with_the_standard_list()
    {
        var profile = Apply(Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AllowMultipleAccessConditions", "true")));
        profile.AllowMultipleRestrictions.Should().BeTrue();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void No_configuration_at_all_is_a_bare_but_valid_profile()
    {
        var profile = Apply(Config());
        profile.RestrictionAxis.Values.Should().BeEmpty();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_profile_document_is_read_from_the_content_root()
    {
        WriteDocument("profiles/test.json", """
            // comments are allowed
            {
              "name": "Test",
              "restrictionAxis": { "label": "Level", "values": [ { "value": "L1", "label": "One" } ] },
              "statements": {
                "label": "Statement", "type": "statement",
                "values": [ { "value": "s1", "label": "S1", "category": "c1", "iiif": "e1" } ],
                "categories": [ { "value": "c1", "label": "C1" } ],
                "iiif": { "entries": [ { "id": "e1", "requiredStatement": "Text" } ] },
                "derived": [ { "type": "tier", "values": [ { "value": "0", "label": "Zero" } ], "matrix": { "*": { "*": "0" } } } ],
              },
            }
            """);
        var profile = Apply(Config(("AccessConditionProfileFile", "profiles/test.json")));

        profile.Name.Should().Be("Test");
        profile.Statements!.Type.Should().Be("statement");
        profile.Statements.IiifRightsFor("s1")!.RequiredStatement.Should().Be("Text");
        profile.Statements.ResolveDerived(["L1"], "s1").Should().Equal(new Dictionary<string, string> { ["tier"] = "0" });
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_deployed_Leeds_document_loads_and_is_valid()
    {
        // The API project's AccessConditionProfiles folder is copied to this test's output.
        var profile = Apply(Config(("AccessConditionProfileFile", "AccessConditionProfiles/leeds.json")), AppContext.BaseDirectory);
        profile.HasStatements.Should().BeTrue();
        profile.Statements!.Derived.Should().ContainSingle(d => d.Type == "tier");
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void A_missing_or_malformed_document_stops_the_service()
    {
        var missing = () => Apply(Config(("AccessConditionProfileFile", "nope.json")));
        missing.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");

        WriteDocument("typo.json", """{ "restrictionAxis": { "lable": "Level" } }""");
        var typo = () => Apply(Config(("AccessConditionProfileFile", "typo.json")));
        typo.Should().Throw<InvalidOperationException>().WithMessage("*could not be read*lable*");
    }

    [Fact]
    public void Both_forms_at_once_is_a_configuration_error()
    {
        WriteDocument("p.json", "{}");
        var act = () => Apply(Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AccessConditionProfileFile", "p.json")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*not both*");

        var multiple = () => Apply(Config(("AllowMultipleAccessConditions", "true"), ("AccessConditionProfileFile", "p.json")));
        multiple.Should().Throw<InvalidOperationException>().WithMessage("*not both*");
    }

    [Fact]
    public void The_retired_inline_section_says_where_the_profile_went()
    {
        var act = () => Apply(Config(("AccessConditionProfile:RestrictionAxis:Label", "Level")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*AccessConditionProfileFile*");
    }
}
