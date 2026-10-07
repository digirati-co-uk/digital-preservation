using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pipeline.API.Features.Pipeline;
using Test.Helpers;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Two endpoints sat outside PipelineController's [ApiKey] gate (September 2026 security review,
/// issue #292 item 2): the bare "/" hello-world route, and Swagger UI/swagger.json, both reachable by
/// anyone since ApiKeyMiddleware only records whether a key was valid - [ApiKey] is what actually
/// refuses a request, and only PipelineController carries it. /health must stay reachable: the load
/// balancer's health check depends on it.
/// </summary>
[Trait("Category", "Integration")]
public class PipelineApiExposureTests
{
    // PipelineJobExecutorService (a BackgroundService) resolves IPipelineQueue, which - unchanged by
    // this issue - is registered twice in Program.cs, so single-instance injection picks the LAST
    // registration: SqsPipelineQueue. Replacing it here keeps the hosted service from making real
    // AWS SQS calls during the test; it never completes, matching an idle queue with nothing to do.
    private static DigitalPreservationAppFactory<Program> BuildFactory() =>
        new DigitalPreservationAppFactory<Program>()
            .WithTestServices(services => services.AddSingleton<IPipelineQueue>(new NeverDequeuesQueue()));

    [Fact]
    public async Task Root_Answers404()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the initial-setup hello-world route must be removed, not merely gated");
    }

    [Fact]
    public async Task Health_StillAnswers200()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the load balancer's health check depends on this path");
    }

    [Fact]
    public async Task Swagger_Answers404_OutsideDevelopment()
    {
        // DigitalPreservationAppFactory sets the ASP.NET Core environment to "Testing", so
        // app.Environment.IsDevelopment() is false here - the same as the deployed dev and prod
        // environments, which set no ASPNETCORE_ENVIRONMENT at all and so also run as Production.
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private class NeverDequeuesQueue : IPipelineQueue
    {
        public ValueTask QueueRequest(string jobIdentifier, string depositName, string? runUser,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Host shutting down - nothing to hand back.
            }
            return null;
        }
    }
}
