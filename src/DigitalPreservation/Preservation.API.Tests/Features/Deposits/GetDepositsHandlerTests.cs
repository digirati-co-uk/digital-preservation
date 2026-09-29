using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// Two independent bugs sharing one handler (issue #263). First: Archived and Active did nothing
/// unless another filter was already present, because DepositQuery.NoTerms() didn't count them,
/// so a query carrying only one of them silently took the handler's own hard-coded active-only
/// default instead of the caller's filter. Second: the four *By filters compared exact strings
/// against the bare caller name, so a full Agent URI from GET /agents - the obvious thing to feed
/// them - matched nothing.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class GetDepositsHandlerTests(DatabaseFixture fixture)
{
    private const string PreservationHost = "https://preservation.test";

    // Generous, and results are ordered by Created descending with these deposits freshly seeded,
    // so pagination never has to compete with whatever other tests in this shared-DB collection
    // have already seeded - it just has to not truncate before reaching the newest rows.
    private const int LargePageSize = 10_000;

    [Fact]
    public async Task Archived_True_Alone_Returns_Exactly_The_Archived_Deposits()
    {
        var (context, active, inactive, archived) = await SeedThreeDeposits();

        var result = await Handle(context, new DepositQuery { Archived = true, PageSize = LargePageSize });

        result.Success.Should().BeTrue();
        var ids = ResultIds(result);
        ids.Should().Contain(DepositUri(archived.MintedId));
        ids.Should().NotContain(DepositUri(active.MintedId));
        ids.Should().NotContain(DepositUri(inactive.MintedId));
    }

    [Fact]
    public async Task Active_False_Alone_Returns_Exactly_The_Inactive_Deposits()
    {
        var (context, active, inactive, archived) = await SeedThreeDeposits();

        var result = await Handle(context, new DepositQuery { Active = false, PageSize = LargePageSize });

        result.Success.Should().BeTrue();
        var ids = ResultIds(result);
        ids.Should().Contain(DepositUri(inactive.MintedId));
        ids.Should().Contain(DepositUri(archived.MintedId), "archived deposits are always inactive too");
        ids.Should().NotContain(DepositUri(active.MintedId));
    }

    [Fact]
    public async Task Archived_False_Alone_Returns_Active_Unarchived_Deposits_Unchanged()
    {
        var (context, active, inactive, archived) = await SeedThreeDeposits();

        var result = await Handle(context, new DepositQuery { Archived = false, PageSize = LargePageSize });

        result.Success.Should().BeTrue();
        var ids = ResultIds(result);
        ids.Should().Contain(DepositUri(active.MintedId));
        ids.Should().NotContain(DepositUri(inactive.MintedId));
        ids.Should().NotContain(DepositUri(archived.MintedId));
    }

    [Fact]
    public async Task An_Empty_Query_Still_Returns_Active_Deposits_Only()
    {
        var (context, active, inactive, archived) = await SeedThreeDeposits();

        var result = await Handle(context, new DepositQuery { PageSize = LargePageSize });

        result.Success.Should().BeTrue();
        var ids = ResultIds(result);
        ids.Should().Contain(DepositUri(active.MintedId));
        ids.Should().NotContain(DepositUri(inactive.MintedId));
        ids.Should().NotContain(DepositUri(archived.MintedId));
    }

    [Fact]
    public async Task CreatedBy_Accepts_A_Full_Agent_Uri_The_Same_As_The_Bare_Name()
    {
        var context = fixture.CreateNewAuthServiceContext();
        var creator = $"creator-{Guid.NewGuid():N}";
        var deposit = await SeedDeposit(context, $"dep-{Guid.NewGuid():N}", creator, creator, active: true, archived: null);

        var byName = await Handle(context, new DepositQuery { CreatedBy = creator, PageSize = LargePageSize });
        var byUri = await Handle(context,
            new DepositQuery { CreatedBy = $"{PreservationHost}/{Agent.BasePathElement}/{creator}", PageSize = LargePageSize });

        byName.Success.Should().BeTrue();
        byUri.Success.Should().BeTrue();
        var namedIds = ResultIds(byName);
        var uriIds = ResultIds(byUri);
        namedIds.Should().Contain(DepositUri(deposit.MintedId));
        uriIds.Should().BeEquivalentTo(namedIds, "a full Agent URI must resolve to the same deposits as the bare name");
    }

    [Fact]
    public async Task LastModifiedBy_Accepts_A_Full_Agent_Uri_The_Same_As_The_Bare_Name()
    {
        var context = fixture.CreateNewAuthServiceContext();
        var modifier = $"modifier-{Guid.NewGuid():N}";
        var deposit = await SeedDeposit(context, $"dep-{Guid.NewGuid():N}", modifier, modifier, active: true, archived: null);

        var byName = await Handle(context, new DepositQuery { LastModifiedBy = modifier, PageSize = LargePageSize });
        var byUri = await Handle(context,
            new DepositQuery { LastModifiedBy = $"{PreservationHost}/{Agent.BasePathElement}/{modifier}", PageSize = LargePageSize });

        byName.Success.Should().BeTrue();
        byUri.Success.Should().BeTrue();
        var namedIds = ResultIds(byName);
        var uriIds = ResultIds(byUri);
        namedIds.Should().Contain(DepositUri(deposit.MintedId));
        uriIds.Should().BeEquivalentTo(namedIds, "a full Agent URI must resolve to the same deposits as the bare name");
    }

    [Fact]
    public async Task CreatedBy_Matching_Stays_Exact_Not_A_Prefix_Search()
    {
        var context = fixture.CreateNewAuthServiceContext();
        var creator = $"creator-{Guid.NewGuid():N}";
        await SeedDeposit(context, $"dep-{Guid.NewGuid():N}", creator, creator, active: true, archived: null);

        var result = await Handle(context,
            new DepositQuery { CreatedBy = creator[..7], PageSize = LargePageSize }); // a genuine prefix of `creator`

        result.Success.Should().BeTrue();
        result.Value!.Deposits.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------

    private async Task<(PreservationContext Context, DepositEntity Active, DepositEntity Inactive, DepositEntity ArchivedAndInactive)>
        SeedThreeDeposits()
    {
        var context = fixture.CreateNewAuthServiceContext();
        var suffix = Guid.NewGuid().ToString("N");

        var active = await SeedDeposit(context, $"active-{suffix}", "alice", "alice", active: true, archived: null);
        var inactive = await SeedDeposit(context, $"inactive-{suffix}", "bob", "bob", active: false, archived: null);
        var archived = await SeedDeposit(context, $"archived-{suffix}", "alice", "alice", active: false, archived: DateTime.UtcNow);

        return (context, active, inactive, archived);
    }

    private static async Task<DepositEntity> SeedDeposit(
        PreservationContext context, string mintedId, string createdBy, string lastModifiedBy, bool active, DateTime? archived)
    {
        var entity = new DepositEntity
        {
            MintedId = mintedId,
            Status = DepositStates.New,
            Active = active,
            Created = DateTime.UtcNow,
            CreatedBy = createdBy,
            LastModified = DateTime.UtcNow,
            LastModifiedBy = lastModifiedBy,
            Archived = archived
        };
        context.Deposits.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    private static Task<DigitalPreservation.Common.Model.Results.Result<DepositQueryPage>> Handle(
        PreservationContext context, DepositQuery? query)
    {
        var handler = new GetDepositsHandler(NullLogger<GetDepositsHandler>.Instance, context, Mutator());
        return handler.Handle(new GetDeposits(query), CancellationToken.None);
    }

    private static List<string> ResultIds(DigitalPreservation.Common.Model.Results.Result<DepositQueryPage> result) =>
        result.Value!.Deposits.Select(d => d.Id!.ToString()).ToList();

    private static string DepositUri(string mintedId) => $"{PreservationHost}/{Deposit.BasePathElement}/{mintedId}";

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = PreservationHost
    }));
}
