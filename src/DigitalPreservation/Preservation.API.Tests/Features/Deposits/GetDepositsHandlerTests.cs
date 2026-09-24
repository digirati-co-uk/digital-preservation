using DigitalPreservation.Common.Model.PreservationApi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

[Collection(DatabaseCollection.CollectionName)]
public class GetDepositsHandlerTests(DatabaseFixture fixture)
{
    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));

    [Fact]
    public async Task Handle_DefaultsPageAndPageSize_WhenQueryHasNoPagingTerms()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var handler = new GetDepositsHandler(new NullLogger<GetDepositsHandler>(), context, Mutator());

        var result = await handler.Handle(new GetDeposits(null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_ReturnsSecondDeposit_WhenPageIsTwoAndPageSizeIsOne()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        // ArchivalGroupPath (no '/') is matched via EndsWith, so a unique suffix on every path
        // scopes the query to just this test's three deposits.
        var uniqueSuffix = $"paging-test-{Guid.NewGuid()}";

        // Default ordering is Created descending, so the third one created (highest Created) is
        // page 1 and the second one created is page 2 when PageSize is 1.
        var now = DateTime.UtcNow;
        var oldest = MakeDeposit($"oldest-{uniqueSuffix}", now.AddMinutes(-2));
        var middle = MakeDeposit($"middle-{uniqueSuffix}", now.AddMinutes(-1));
        var newest = MakeDeposit($"newest-{uniqueSuffix}", now);
        context.Deposits.AddRange(oldest, middle, newest);
        await context.SaveChangesAsync();

        var handler = new GetDepositsHandler(new NullLogger<GetDepositsHandler>(), context, Mutator());
        var query = new DepositQuery { Page = 2, PageSize = 1, ArchivalGroupPath = uniqueSuffix };

        var result = await handler.Handle(new GetDeposits(query), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Deposits.Should().ContainSingle();
        result.Value.Deposits[0].ArchivalGroup!.ToString().Should().EndWith(middle.ArchivalGroupPathUnderRoot!);
    }

    private static DepositEntity MakeDeposit(string archivalGroupPathUnderRoot, DateTime created) => new()
    {
        MintedId = $"dep-{Guid.NewGuid()}",
        ArchivalGroupPathUnderRoot = archivalGroupPathUnderRoot,
        Status = "preserved",
        Active = true,
        Created = created,
        CreatedBy = "tester",
        LastModified = created,
        LastModifiedBy = "tester"
    };
}
