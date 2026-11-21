using System.Security.Claims;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using FakeItEasy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits;
using Preservation.API.Features.Deposits.PipelineDelivery;
using Preservation.API.Features.Identity;
using Storage.Repository.Common;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// Issue #410: the one rule for which deposits may run the pipeline, shared by RunPipelineHandler
/// (which enforces it, see <see cref="RunPipelineHandlerTests"/>) and /whoami (which reports it).
/// </summary>
public class PipelinePolicyTests
{
    private const string DefaultBucket = "dev-deposits";
    private const string EthesesBucket = "leeds-etheses-deposits";
    private const string GoobiBucket = "leeds-goobi-deposits";
    private const string EthesesAppId = "33333333-3333-3333-3333-333333333333";
    private const string GoobiAppId = "22222222-2222-2222-2222-222222222222";

    private static readonly IClientDirectory Clients = new ClientDirectory(new Dictionary<string, ClientProfile>
    {
        [EthesesAppId] = new() { Name = "etheses", DepositBucket = EthesesBucket, AllowPipeline = true },
        [GoobiAppId] = new() { Name = "goobi", DepositBucket = GoobiBucket }
    });

    [Theory]
    [InlineData(DefaultBucket, false, PipelineAccess.Permitted)]
    [InlineData(DefaultBucket, true, PipelineAccess.Permitted)]
    [InlineData(EthesesBucket, false, PipelineAccess.NotReachable)]
    [InlineData(EthesesBucket, true, PipelineAccess.Permitted)]
    [InlineData(GoobiBucket, false, PipelineAccess.NotReachable)]
    [InlineData(GoobiBucket, true, PipelineAccess.NotPermitted)]
    [InlineData("a-bucket-no-client-routes-to", true, PipelineAccess.NotPermitted)]
    [InlineData(null, true, PipelineAccess.NotPermitted)]
    public void ForBucket(string? bucket, bool allowCallerBuckets, PipelineAccess expected)
    {
        PipelinePolicy.ForBucket(bucket, DefaultBucket, allowCallerBuckets, Clients).Should().Be(expected);
    }

    private static WhoAmIResult WhoAmI(ClaimsPrincipal user, bool allowCallerBuckets)
    {
        var controller = new WhoAmIController(
            Clients,
            Options.Create(new AwsStorageOptions { DefaultWorkingBucket = DefaultBucket }),
            Options.Create(new PipelineDeliveryFlags { AllowPipelineForCallerBuckets = allowCallerBuckets }),
            FeatureFlags())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        var ok = controller.Get(header: null).Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<WhoAmIResult>().Subject;
    }

    private static IOptionsMonitor<FeatureFlagOptions> FeatureFlags()
    {
        var monitor = A.Fake<IOptionsMonitor<FeatureFlagOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new FeatureFlagOptions());
        return monitor;
    }

    private static ClaimsPrincipal Machine(string appId) =>
        new(new ClaimsIdentity([new Claim("azp", appId)], "test"));

    [Theory]
    [InlineData(EthesesAppId, true, EthesesBucket, true)]
    [InlineData(EthesesAppId, false, EthesesBucket, false)]
    [InlineData(GoobiAppId, true, GoobiBucket, false)]
    public void WhoAmI_Reports_Whether_The_Callers_Deposits_May_Run_The_Pipeline(
        string appId, bool allowCallerBuckets, string expectedBucket, bool expectedAllowed)
    {
        var result = WhoAmI(Machine(appId), allowCallerBuckets);

        result.DepositBucket.Should().Be(expectedBucket);
        result.PipelineAllowed.Should().Be(expectedAllowed);
    }

    [Fact]
    public void WhoAmI_Reports_The_Pipeline_Allowed_For_A_Caller_In_The_Default_Bucket()
    {
        var result = WhoAmI(Machine("55555555-5555-5555-5555-555555555555"), allowCallerBuckets: false);

        result.DepositBucket.Should().Be(DefaultBucket);
        result.PipelineAllowed.Should().BeTrue();
    }
}
