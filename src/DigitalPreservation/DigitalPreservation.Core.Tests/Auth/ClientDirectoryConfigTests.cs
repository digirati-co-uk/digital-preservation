using DigitalPreservation.Core.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Tests.Auth;

/// <summary>
/// Characterises how <see cref="ClientDirectoryServiceCollectionExtensions.AddClientDirectory"/>
/// binds the <c>KnownClients</c> section — in particular that malformed config fails at startup
/// instead of silently mis-resolving callers: the binder cannot enforce <c>ClientProfile</c>'s
/// <c>required Name</c>, and a flat <c>"appId": "name"</c> string entry would otherwise be dropped.
/// </summary>
public class ClientDirectoryConfigTests
{
    private const string Goobi = "22222222-2222-2222-2222-222222222222";

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static IClientDirectory Register(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddClientDirectory(configuration);
        return services.BuildServiceProvider().GetRequiredService<IClientDirectory>();
    }

    [Fact]
    public void ObjectForm_BindsAndResolves()
    {
        var directory = Register(Config(
            ($"KnownClients:{Goobi}:name", "goobi"),
            ($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits")));

        directory.TryResolve(Goobi, out var profile).Should().BeTrue();
        profile!.Name.Should().Be("goobi");
        profile.DepositBucket.Should().Be("leeds-goobi-deposits");
    }

    [Fact]
    public void MissingSection_YieldsEmptyDirectory()
    {
        var directory = Register(Config(("Unrelated", "value")));

        directory.TryResolve(Goobi, out _).Should().BeFalse();
    }

    [Fact]
    public void FlatStringForm_FailsAtStartup()
    {
        // "appId": "name" is the shape an early RFC-0001 draft showed; it must be rejected loudly.
        var act = () => Register(Config(($"KnownClients:{Goobi}", "goobi")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{Goobi}*")
            .WithMessage("*objects*");
    }

    [Fact]
    public void ObjectWithoutName_FailsAtStartup()
    {
        var act = () => Register(Config(($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits")));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Goobi}*");
    }

    private const string Etheses = "33333333-3333-3333-3333-333333333333";
    private const string Other = "44444444-4444-4444-4444-444444444444";

    [Fact]
    public void AllowPipeline_Binds_And_Defaults_Off()
    {
        var directory = Register(Config(
            ($"KnownClients:{Goobi}:name", "goobi"),
            ($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits"),
            ($"KnownClients:{Etheses}:name", "etheses"),
            ($"KnownClients:{Etheses}:depositBucket", "leeds-etheses-deposits"),
            ($"KnownClients:{Etheses}:allowPipeline", "true")));

        directory.TryResolve(Goobi, out var goobi).Should().BeTrue();
        goobi!.AllowPipeline.Should().BeFalse("a caller's deposits run the pipeline only when it is switched on (issue #410)");
        directory.TryResolve(Etheses, out var etheses).Should().BeTrue();
        etheses!.AllowPipeline.Should().BeTrue();
    }

    [Fact]
    public void IsPipelineAllowedForDepositBucket_Follows_The_Routed_Callers_Profiles()
    {
        var directory = Register(Config(
            ($"KnownClients:{Goobi}:name", "goobi"),
            ($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits"),
            ($"KnownClients:{Etheses}:name", "etheses"),
            ($"KnownClients:{Etheses}:depositBucket", "leeds-etheses-deposits"),
            ($"KnownClients:{Etheses}:allowPipeline", "true")));

        directory.IsPipelineAllowedForDepositBucket("leeds-etheses-deposits").Should().BeTrue();
        directory.IsPipelineAllowedForDepositBucket("leeds-goobi-deposits").Should().BeFalse();
        directory.IsPipelineAllowedForDepositBucket("a-bucket-no-client-routes-to").Should().BeFalse();
        directory.IsPipelineAllowedForDepositBucket(null).Should().BeFalse();
        directory.IsPipelineAllowedForDepositBucket("").Should().BeFalse();
    }

    [Fact]
    public void IsPipelineAllowedForDepositBucket_Is_False_When_Any_Caller_Sharing_The_Bucket_Is_Excluded()
    {
        // Enabling one caller must never open the pipeline to another caller's deposits in the same bucket.
        var directory = Register(Config(
            ($"KnownClients:{Etheses}:name", "etheses"),
            ($"KnownClients:{Etheses}:depositBucket", "shared-deposits"),
            ($"KnownClients:{Etheses}:allowPipeline", "true"),
            ($"KnownClients:{Other}:name", "other"),
            ($"KnownClients:{Other}:depositBucket", "shared-deposits")));

        directory.IsPipelineAllowedForDepositBucket("shared-deposits").Should().BeFalse();
    }
}
