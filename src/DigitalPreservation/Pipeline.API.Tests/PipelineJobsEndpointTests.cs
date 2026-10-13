using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pipeline.API.Features.Pipeline;
using Test.Helpers;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// POST /pipeline/jobs (issue #352): the internal enqueue endpoint the Preservation API calls
/// instead of publishing to SNS when FeatureFlags:UseLocalPipelineDelivery is on, local
/// development/testing only. Must 404 wherever FeatureFlags:UseLocalHostedServiceForPipeline is
/// off - every deployed environment - rather than being a second, always-reachable way to queue a
/// job; [ApiKey] still applies underneath that regardless.
/// </summary>
[Trait("Category", "Integration")]
public class PipelineJobsEndpointTests
{
    private const string ValidApiKey = "API Key"; // matches appsettings.Testing.json's ApiKeyOptions:ApiKey

    // PipelineJobExecutorService (a BackgroundService) would otherwise race these tests' own
    // DequeueRequest calls for whatever this endpoint just enqueued into the same
    // InProcessPipelineQueue singleton - whichever reader wins is undefined, so the test would be
    // flaky rather than wrong. It plays no part in what these tests are about (the enqueue step,
    // not execution), so it is removed rather than worked around.
    private static DigitalPreservationAppFactory<Program> BuildFactory(bool localHostedServiceForPipeline) =>
        new DigitalPreservationAppFactory<Program>()
            .WithConfigValue("FeatureFlags:UseLocalHostedServiceForPipeline", localHostedServiceForPipeline ? "true" : "false")
            // appsettings.Testing.json has no Preservation:Root - nothing constructed
            // PipelineController through the full DI container before this file, since
            // PipelineControllerTests.cs builds it directly rather than via the WebApplicationFactory.
            // IPreservationApiClient is a constructor dependency of PipelineController regardless of
            // which action is called, so even QueuePipelineJob (which never uses it) needs this set
            // merely to let the controller be constructed. Any syntactically valid URL will do - the
            // client is never actually called by these tests.
            .WithConfigValue("Preservation:Root", "https://localhost/")
            .WithTestServices(services =>
            {
                var executorDescriptors = services
                    .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PipelineJobExecutorService))
                    .ToList();
                foreach (var descriptor in executorDescriptors)
                {
                    services.Remove(descriptor);
                }
            });

    private static HttpClient ClientWithApiKey(DigitalPreservationAppFactory<Program> factory, string? apiKey = ValidApiKey)
    {
        var client = factory.CreateClient();
        if (apiKey != null)
        {
            client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
        }
        return client;
    }

    [Fact]
    public async Task Returns404_WhenUseLocalHostedServiceForPipelineIsOff()
    {
        using var factory = BuildFactory(localHostedServiceForPipeline: false);
        var client = ClientWithApiKey(factory);

        var response = await client.PostAsJsonAsync("/pipeline/jobs",
            new PipelineJobMessage { DepositName = "dep-1", JobIdentifier = "job-1" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the endpoint must not be reachable wherever local pipeline delivery is off - every deployed environment");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong key")]
    public async Task Refuses_MissingOrWrongApiKey_EvenWithTheFlagOn(string? apiKey)
    {
        using var factory = BuildFactory(localHostedServiceForPipeline: true);
        var client = ClientWithApiKey(factory, apiKey);

        var response = await client.PostAsJsonAsync("/pipeline/jobs",
            new PipelineJobMessage { DepositName = "dep-1", JobIdentifier = "job-1" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WritesToTheInProcessQueue_WhenTheFlagIsOn_AndDequeueReturnsIt()
    {
        using var factory = BuildFactory(localHostedServiceForPipeline: true);
        var client = ClientWithApiKey(factory);

        var response = await client.PostAsJsonAsync("/pipeline/jobs",
            new PipelineJobMessage { DepositName = "dep-1", JobIdentifier = "job-1", RunUser = "tester" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // InProcessPipelineQueue is always registered (Program.cs), regardless of which
        // IPipelineQueue implementation this instance is actually using - fetch it directly to
        // prove the message really landed in the channel, not just that the endpoint returned 204.
        var queue = factory.Services.GetRequiredService<InProcessPipelineQueue>();
        using var dequeueTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var dequeued = await queue.DequeueRequest(dequeueTimeout.Token);

        dequeued.Should().NotBeNull();
        dequeued!.DepositName.Should().Be("dep-1");
        dequeued.JobIdentifier.Should().Be("job-1");
        dequeued.RunUser.Should().Be("tester");
    }

    [Fact]
    public async Task Returns400_WhenDepositNameIsMissing()
    {
        using var factory = BuildFactory(localHostedServiceForPipeline: true);
        var client = ClientWithApiKey(factory);

        var response = await client.PostAsJsonAsync("/pipeline/jobs",
            new PipelineJobMessage { DepositName = "", JobIdentifier = "job-1" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns400_WhenJobIdentifierIsMissing()
    {
        using var factory = BuildFactory(localHostedServiceForPipeline: true);
        var client = ClientWithApiKey(factory);

        var response = await client.PostAsJsonAsync("/pipeline/jobs",
            new PipelineJobMessage { DepositName = "dep-1" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
