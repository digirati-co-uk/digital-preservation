using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using Microsoft.Extensions.Logging.Abstractions;
using DepositEntity = Preservation.API.Data.Entities.Deposit;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Tests.TestingInfrastructure;

namespace Preservation.API.Tests.Features.Deposits;

[Collection(DatabaseCollection.CollectionName)]
public class ActivateDepositHandlerTests
{
    private readonly DatabaseFixture fixture;

    public ActivateDepositHandlerTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Handle_Fails_When_DeactivatingNewDeposit()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var mintedId = $"dep-{Guid.NewGuid()}";
        var deposit = new DepositEntity
        {
            MintedId = mintedId,
            Status = DepositStates.New,
            Active = true,
            Created = DateTime.UtcNow,
            CreatedBy = "tester",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "tester"
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync();

        var handler = new ActivateDepositHandler(new NullLogger<ActivateDepositHandler>(), context);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test"));

        var result = await handler.Handle(new ActivateDeposit(mintedId, false, principal), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);

        await context.Entry(deposit).ReloadAsync();
        deposit.Active.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SetsActive_WhenDepositExists()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var mintedId = $"dep-{Guid.NewGuid()}";
        var deposit = new DepositEntity
        {
            MintedId = mintedId,
            Status = DepositStates.Exporting,
            Active = false,
            Created = DateTime.UtcNow,
            CreatedBy = "tester",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "tester"
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync();

        var handler = new ActivateDepositHandler(new NullLogger<ActivateDepositHandler>(), context);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test"));

        var result = await handler.Handle(new ActivateDeposit(mintedId, true, principal), CancellationToken.None);

        result.Success.Should().BeTrue();

        await context.Entry(deposit).ReloadAsync();
        deposit.Active.Should().BeTrue();
    }
}
