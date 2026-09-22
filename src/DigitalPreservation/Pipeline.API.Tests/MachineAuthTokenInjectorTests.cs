using System.Net;
using DigitalPreservation.Core.Web.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Preservation.Client;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Pins the injector's contract with <see cref="IAccessTokenProvider"/>, whose failure mode changed
/// on PR #284: a failed mint now THROWS (and is never cached) instead of surfacing null. The
/// injector must let that throw propagate — the alternative is exactly the incident class the
/// provider change closed: requests going out unauthenticated, silently, and 401ing downstream.
/// </summary>
public class MachineAuthTokenInjectorTests
{
    [Fact]
    public async Task AThrowingProvider_Propagates_AndNothingIsSent()
    {
        var inner = new CountingHandler();
        var injector = BuildInjector(new ThrowingProvider(), inner);
        using var invoker = new HttpMessageInvoker(injector);

        var act = () => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://preservation.example/api/deposits"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        inner.CallCount.Should().Be(0, "a request that could not be authenticated must never be sent");
    }

    [Fact]
    public async Task AMintedToken_IsAttachedAsBearer()
    {
        var inner = new CountingHandler();
        var injector = BuildInjector(new FixedProvider("test-token"), inner);
        using var invoker = new HttpMessageInvoker(injector);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://preservation.example/api/deposits"),
            CancellationToken.None);

        inner.CallCount.Should().Be(1);
        inner.LastAuthorization.Should().Be("Bearer test-token");
    }

    private static MachineAuthTokenInjector BuildInjector(IAccessTokenProvider provider, CountingHandler inner) =>
        new(provider, NullLogger<MachineAuthTokenInjector>.Instance) { InnerHandler = inner };

    private class ThrowingProvider : IAccessTokenProvider
    {
        public Task<string?> GetAccessToken() =>
            throw new InvalidOperationException("Token endpoint answered 200 without an access_token");
    }

    private class FixedProvider(string token) : IAccessTokenProvider
    {
        public Task<string?> GetAccessToken() => Task.FromResult<string?>(token);
    }

    private class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
