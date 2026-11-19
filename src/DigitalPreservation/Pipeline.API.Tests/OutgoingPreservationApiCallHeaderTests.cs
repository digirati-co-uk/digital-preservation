using System.Net;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pipeline.API.Features.Pipeline;
using Preservation.Client;
using Test.Helpers;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Verifies a review concern raised against issue #388 item 1.7 (PR #402, which added
/// AddCorrelationIdHeaderPropagation to Pipeline.API): PropagateCorrelationIdHandler is attached to
/// every HttpClient the factory builds, including the one AddMachinePreservationClient already
/// configures with its own X-Client-Identity default header and its own MachineAuthTokenInjector.
/// For a call with no ambient HttpContext - true of Pipeline.API's SQS-triggered background work,
/// which is exactly what calls IPreservationApiClient - PropagateCorrelationIdHandler's own
/// SetMachineToken path also writes X-Client-Identity and Authorization. The question was whether
/// that duplicates either header on the wire rather than replacing it. Answered here by building the
/// real DI-configured HttpClient (the same one production code sends through) and inspecting the
/// request a capturing primary handler actually receives, instead of reasoning about
/// IHttpMessageHandlerBuilderFilter/HttpClientFactory ordering from the source.
/// </summary>
public class OutgoingPreservationApiCallHeaderTests
{
    private static DigitalPreservationAppFactory<Program> BuildFactory(HttpMessageHandler capturingHandler) =>
        new DigitalPreservationAppFactory<Program>()
            // Re-registering IPreservationApiClient re-runs AddMachinePreservationClient's client
            // config lambda, which requires Preservation:Root - not set in appsettings.Testing.json
            // since nothing else resolves this client there.
            .WithConfigValue("Preservation:Root", "https://preservation.test/")
            .WithTestServices(services =>
            {
                // Same substitution CorrelationIdTests uses: PipelineJobExecutorService resolves
                // IPipelineQueue, and the real SqsPipelineQueue would make live AWS calls on host start.
                services.AddSingleton<IPipelineQueue>(new NeverDequeuesQueue());
                // A real token lets SetMachineToken's Authorization branch actually run - with no
                // AAD config in appsettings.Testing.json, AccessTokenProvider.GetAccessToken would
                // otherwise return null and that branch would be a no-op, hiding the question asked.
                services.AddSingleton<IAccessTokenProvider>(new FixedTokenProvider("fake-machine-token"));
                // Layers onto the client AddMachinePreservationClient already configured in
                // Program.cs (same typed-client registration, same name) - every handler production
                // code adds (TimingHandler, MachineAuthTokenInjector, and the correlation filter) stays
                // in place; only the innermost transport is replaced, so nothing reaches the network.
                services.AddHttpClient<IPreservationApiClient, PreservationApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => capturingHandler);
            });

    [Fact]
    public async Task A_Background_Call_Does_Not_Duplicate_The_Client_Identity_Or_Authorization_Header()
    {
        HttpRequestMessage? captured = null;
        var capturingHandler = new CapturingHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var factory = BuildFactory(capturingHandler);
        using var scope = factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IPreservationApiClient>();

        // No ambient HttpContext here, by construction - a scope resolved directly in a test is
        // exactly the "no incoming request" shape of Pipeline.API's SQS-triggered background calls.
        await client.GetDeposit("some-deposit", TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Headers.TryGetValues(AuthFilterIdentifier.MachineHeaderName, out var clientIdentityValues)
            .Should().BeTrue();
        clientIdentityValues!.Should().ContainSingle()
            .Which.Should().Be("PipelineAPI", "AddMachinePreservationClient's own default header should win, not be duplicated");

        captured.Headers.Authorization.Should().NotBeNull();
        captured.Headers.TryGetValues("Authorization", out var authValues).Should().BeTrue();
        authValues!.Should().ContainSingle("only MachineAuthTokenInjector's token should reach the wire");
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FixedTokenProvider(string token) : IAccessTokenProvider
    {
        public Task<string?> GetAccessToken(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(token);
    }

    // Copied from CorrelationIdTests: PipelineJobExecutorService needs a queue that never produces
    // work, so the host doesn't touch real SQS when it starts as part of building the factory.
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
