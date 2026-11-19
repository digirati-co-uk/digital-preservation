using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pipeline.API.Features.Pipeline;
using Test.Helpers;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Pipeline.API used to skip the correlation-ID setup every other host has (issue #388 item 1.7):
/// no CorrelationIdMiddleware, no AddCorrelationIdHeaderPropagation, and no
/// Enrich.WithCorrelationId() in its Serilog setup, despite CLAUDE.md saying every service should
/// have it. These pin the middleware's observable effect - the one piece reachable from outside
/// the process without inspecting log output.
/// </summary>
[Trait("Category", "Integration")]
public class CorrelationIdTests
{
    // Same substitution as PipelineApiExposureTests: PipelineJobExecutorService resolves
    // IPipelineQueue, and the real SqsPipelineQueue would make live AWS calls during the test.
    private static DigitalPreservationAppFactory<Program> BuildFactory() =>
        new DigitalPreservationAppFactory<Program>()
            .WithTestServices(services => services.AddSingleton<IPipelineQueue>(new NeverDequeuesQueue()));

    [Fact]
    public async Task A_Response_Always_Carries_A_Correlation_Id()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.Headers.Should().ContainKey("x-correlation-id");
        response.Headers.GetValues("x-correlation-id").Should().ContainSingle()
            .Which.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_Correlation_Id_Supplied_On_The_Request_Is_Echoed_Back()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("x-correlation-id", "test-correlation-id");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("x-correlation-id").Should().ContainSingle()
            .Which.Should().Be("test-correlation-id");
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
