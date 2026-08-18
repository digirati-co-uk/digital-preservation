using System.Security.Claims;
using DigitalPreservation.Core.Auth;

namespace DigitalPreservation.Core.Tests.Auth;

public class CallerResolverTests
{
    private const string Goobi = "22222222-2222-2222-2222-222222222222";
    private const string Iiif = "11111111-1111-1111-1111-111111111111";
    private const string DefaultBucket = "dev-deposits";

    private static ClientDirectory Directory() => new(new Dictionary<string, ClientProfile>
    {
        [Goobi] = new() { Name = "goobi", DepositBucket = "leeds-goobi-deposits" },
        [Iiif] = new() { Name = "iiif-builder" }
    });

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void HumanCaller_ResolvesToUser_WithDefaultBucket()
    {
        var result = CallerResolver.Resolve(
            Principal(("preferred_username", "alice@leeds.ac.uk"), ("azp", "some-client-app")),
            clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceUser);
        result.Name.Should().Be("alice@leeds.ac.uk");
        result.AppId.Should().Be("some-client-app");
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void KnownMachine_ResolvesFromToken_WithProfileBucket()
    {
        var result = CallerResolver.Resolve(
            Principal(("azp", Goobi)),
            clientIdentityHeader: "spoofed-header", Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceToken);
        result.Name.Should().Be("goobi");
        result.AppId.Should().Be(Goobi);
        result.DepositBucket.Should().Be("leeds-goobi-deposits"); // signed token wins over the header
    }

    [Fact]
    public void KnownMachine_WithoutProfileBucket_FallsBackToDefaultBucket()
    {
        var result = CallerResolver.Resolve(
            Principal(("appid", Iiif)), // appid is the v1 spelling of azp
            clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceToken);
        result.Name.Should().Be("iiif-builder");
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void UnknownMachineWithHeader_FallsBackToHeader()
    {
        var result = CallerResolver.Resolve(
            Principal(("azp", "99999999-9999-9999-9999-999999999999")),
            clientIdentityHeader: "legacy-caller", Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceHeaderFallback);
        result.Name.Should().Be("legacy-caller");
        result.AppId.Should().Be("99999999-9999-9999-9999-999999999999");
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void NoClaimsNoHeader_ResolvesToUnknown()
    {
        var result = CallerResolver.Resolve(
            Principal(), clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceUnknown);
        result.Name.Should().Be("unknown");
        result.AppId.Should().BeNull();
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void NullDirectory_FallsBackToHeader()
    {
        var result = CallerResolver.Resolve(
            Principal(("azp", Goobi)), clientIdentityHeader: "header-name", clients: null, DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceHeaderFallback);
        result.Name.Should().Be("header-name");
    }

    [Fact]
    public void ResolveDepositBucket_KnownMachine_GetsProfileBucket()
    {
        CallerResolver.ResolveDepositBucket(Principal(("azp", Goobi)), Directory())
            .Should().Be("leeds-goobi-deposits");
    }

    [Fact]
    public void ResolveDepositBucket_KnownMachineWithoutProfileBucket_GetsNull()
    {
        CallerResolver.ResolveDepositBucket(Principal(("appid", Iiif)), Directory())
            .Should().BeNull();
    }

    [Fact]
    public void ResolveDepositBucket_HumanCaller_GetsNull_EvenWhenSignInAppIsKnown()
    {
        // A human signing in through an app that happens to be in KnownClients still gets the
        // default bucket - profile buckets are for the machine caller itself.
        CallerResolver.ResolveDepositBucket(
                Principal(("preferred_username", "alice@leeds.ac.uk"), ("azp", Goobi)), Directory())
            .Should().BeNull();
    }

    [Fact]
    public void V1Human_WithUpnClaim_ResolvesToUser_WithDefaultBucket()
    {
        // v1.0-format delegated tokens carry upn/unique_name and no preferred_username. The human
        // predicate must still classify them as human (RFC-0001 §8 Q7), or /whoami misreports and
        // ResolveDepositBucket could route a human into a per-caller bucket.
        var result = CallerResolver.Resolve(
            Principal(("upn", "alice@leeds.ac.uk"), ("azp", Goobi)),
            clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceUser);
        result.Name.Should().Be("alice@leeds.ac.uk");
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void V1Human_WithMappedUniqueName_ResolvesToUser()
    {
        // unique_name is mapped to ClaimTypes.Name by the default inbound claim mapping.
        var result = CallerResolver.Resolve(
            Principal((ClaimTypes.Name, "Alice Smith"), ("azp", Goobi)),
            clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceUser);
        result.DepositBucket.Should().Be(DefaultBucket);
    }

    [Fact]
    public void ResolveDepositBucket_V1Human_GetsNull_EvenWhenSignInAppIsKnown()
    {
        CallerResolver.ResolveDepositBucket(
                Principal(("upn", "alice@leeds.ac.uk"), ("azp", Goobi)), Directory())
            .Should().BeNull();
    }

    [Fact]
    public void MachineWithInjectedNameClaim_StillResolvesAsMachine()
    {
        // AuthFilterIdentifier injects a literal "Name" claim for machine callers before /whoami
        // runs; that claim must not flip the caller to human (it is not a user-identifying claim).
        var result = CallerResolver.Resolve(
            Principal(("azp", Goobi), ("Name", "goobi")),
            clientIdentityHeader: null, Directory(), DefaultBucket);

        result.Source.Should().Be(CallerResolver.SourceToken);
        result.Name.Should().Be("goobi");
        result.DepositBucket.Should().Be("leeds-goobi-deposits");
    }

    [Fact]
    public void ResolveDepositBucket_UnknownMachine_GetsNull()
    {
        CallerResolver.ResolveDepositBucket(
                Principal(("azp", "99999999-9999-9999-9999-999999999999")), Directory())
            .Should().BeNull();
    }

    [Fact]
    public void ResolveDepositBucket_NullDirectory_GetsNull()
    {
        CallerResolver.ResolveDepositBucket(Principal(("azp", Goobi)), clients: null)
            .Should().BeNull();
    }
}
