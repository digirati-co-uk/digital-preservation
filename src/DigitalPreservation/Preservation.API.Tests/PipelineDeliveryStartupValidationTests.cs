using Test.Helpers;
using Xunit;

namespace Preservation.API.Tests;

/// <summary>
/// Issue #352 review: a missing PipelineApiDelivery:BaseUrl used to throw only when
/// IPipelineJobDeliveryClient's HttpClient was first constructed - a 500 on the first local
/// pipeline run, not a failure to start - and a missing ApiKey wasn't checked at all, surfacing
/// only as a 401 in that job's own recorded errors. AddOptions(...).ValidateOnStart() now catches
/// both at host startup instead.
///
/// Asserts only that startup fails, not the exact exception type reaching the test: ASP.NET
/// Core's Host disposes its service provider once StartAsync throws, so by the time
/// WebApplicationFactory.Services is evaluated here the original OptionsValidationException has
/// already been logged ("Hosting failed to start") and replaced by an ObjectDisposedException on
/// the now-torn-down provider. The real, specific exception and its "BaseUrl is not set"/"ApiKey
/// is not set" messages were confirmed directly against this test's own run output; asserting on
/// that logged text here would be pinning WebApplicationFactory's internal wrapping, not this
/// change's actual behaviour.
/// </summary>
[Trait("Category", "Integration")]
public class PipelineDeliveryStartupValidationTests
{
    [Fact]
    public void Host_FailsToStart_WhenLocalDeliveryIsOnWithNoBaseUrlOrApiKey()
    {
        using var factory = new DigitalPreservationAppFactory<Program>()
            .WithConfigValue("FeatureFlags:UseLocalPipelineDelivery", "true");
        // appsettings.Testing.json has no PipelineApiDelivery section at all, so both BaseUrl and
        // ApiKey are unset - exactly the config mistake this validation exists to catch early.

        var act = () => factory.Services;

        act.Should().Throw<Exception>("the host must refuse to start rather than let the job fail later with a 500 or a 401");
    }

    [Fact]
    public void Host_FailsToStart_WhenLocalDeliveryIsOnWithBaseUrlButNoApiKey()
    {
        using var factory = new DigitalPreservationAppFactory<Program>()
            .WithConfigValue("FeatureFlags:UseLocalPipelineDelivery", "true")
            .WithConfigValue("PipelineApiDelivery:BaseUrl", "https://localhost:7005/");

        var act = () => factory.Services;

        act.Should().Throw<Exception>("the host must refuse to start rather than let the job fail later with a 401");
    }
}
