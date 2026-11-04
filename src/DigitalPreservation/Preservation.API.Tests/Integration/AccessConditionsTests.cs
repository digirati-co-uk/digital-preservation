using System.Net;
using System.Text.Json;
using DigitalPreservation.Common.Model.AccessConditions;
using Test.Helpers;

namespace Preservation.API.Tests.Integration;

/// <summary>
/// <c>GET /access-conditions</c> (issue #433) returns the configured <see cref="AccessConditionProfile"/>
/// in the camelCase shape the documentation site and the UI's client expect.
/// </summary>
[Trait("Category", "Integration")]
public class AccessConditionsTests(DigitalPreservationAppFactory<Program> factory) : IClassFixture<DigitalPreservationAppFactory<Program>>
{
    private readonly HttpClient httpClient = factory.CreateClient();

    [Fact]
    public async Task Access_conditions_returns_a_valid_camelCase_profile()
    {
        var response = await httpClient.GetAsync("/access-conditions", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object, "it is a profile, not the pre-#433 list");
        root.TryGetProperty("allowMultipleRestrictions", out _).Should().BeTrue();
        root.GetProperty("restrictionAxis").GetProperty("values").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("rightsAxis").GetProperty("label").ValueKind.Should().Be(JsonValueKind.String);
        // categoryAxis is present only on an installation that derives something; the test
        // environment inherits the Leeds profile from appsettings.json.
        if (root.TryGetProperty("categoryAxis", out var axis))
            axis.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("derived").ValueKind.Should().Be(JsonValueKind.Array);

        // Round-trips through the model the client deserialises into, and is a profile the API
        // itself would have accepted at startup.
        var profile = JsonSerializer.Deserialize<AccessConditionProfile>(json);
        profile.Should().NotBeNull();
        profile!.Validate().Should().BeEmpty();
    }
}
