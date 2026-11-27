using System.Text.Json;
using DigitalPreservation.Common.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Preservation.API.Mutation;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Mutation;

/// <summary>
/// An Archival Group, and a Deposit when its IIIF view is switched on, link to their IIIF
/// projections (issue #261).
/// </summary>
public class ResourceMutatorIiifLinksTests
{
    private static ResourceMutator Mutator(bool depositIiif = false) => new(
        Options.Create(new MutatorOptions { Storage = "https://storage.test", Preservation = "https://preservation.test" }),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FeatureFlags:EnableIiifMediaEndpoints"] = depositIiif.ToString() })
            .Build());

    [Fact]
    public void An_Archival_Group_Links_To_Both_Iiif_Views()
    {
        var group = new ArchivalGroup { Id = new Uri("https://storage.test/repository/cc/my%20thing") };

        Mutator().MutateStorageResource(group);

        group.Iiif.Should().Be(new Uri("https://preservation.test/repository/cc/my%20thing?view=iiif"));
        group.IiifCs.Should().Be(new Uri("https://preservation.test/repository/cc/my%20thing?view=iiif-cs"));
    }

    [Fact]
    public void Only_An_Archival_Group_Gets_Them()
    {
        var container = new Container { Id = new Uri("https://storage.test/repository/cc") };

        Mutator().MutateStorageResource(container);

        var json = JsonSerializer.Serialize<PreservedResource>(container);
        json.Should().NotContain("\"iiif");
    }

    [Fact]
    public void They_Are_Not_Sent_On_To_The_Storage_Api()
    {
        var group = new ArchivalGroup
        {
            Id = new Uri("https://preservation.test/repository/cc/thing"),
            Iiif = new Uri("https://preservation.test/repository/cc/thing?view=iiif"),
            IiifCs = new Uri("https://preservation.test/repository/cc/thing?view=iiif-cs")
        };

        Mutator().MutatePreservationResource(group);

        group.Iiif.Should().BeNull();
        group.IiifCs.Should().BeNull();
    }

    [Theory]
    [InlineData(true, "https://preservation.test/deposits/abc123/iiif")]
    [InlineData(false, null)]
    public void A_Deposit_Links_To_Its_Iiif_View_Only_When_The_View_Is_Switched_On(bool switchedOn, string? expected)
    {
        var entity = new DepositEntity
        {
            MintedId = "abc123",
            Files = new Uri("s3://bucket/deposits/abc123/"),
            Status = "new",
            Created = DateTime.UtcNow,
            CreatedBy = "someone",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "someone"
        };

        var deposit = Mutator(switchedOn).MutateDeposit(entity);

        deposit.Iiif.Should().Be(expected == null ? null : new Uri(expected));
    }
}
