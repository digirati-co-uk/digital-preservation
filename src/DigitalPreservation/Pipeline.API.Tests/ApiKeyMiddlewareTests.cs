using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Middleware;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// The API key comparison switched from <c>StringValues == string</c> (which short-circuits at the
/// first differing character, leaking key-prefix correctness via response timing on the private VPC
/// this API runs behind) to <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>.
/// These pin the observable outcome - constant-time behaviour itself isn't something a unit test can
/// assert, but every case that mattered under the old comparison must still resolve the same way.
/// </summary>
public class ApiKeyMiddlewareTests
{
    private const string HeaderName = "X-API-KEY";
    private const string RealKey = "correct-key-12345";

    private static ApiKeyMiddleware Middleware(string? configuredKey = RealKey, string? headerName = HeaderName) =>
        new(Options.Create(new ApiKeyOptions { ApiKey = configuredKey, ApiHeaderName = headerName }));

    private static async Task<object?> Invoke(ApiKeyMiddleware middleware, string? suppliedKey)
    {
        var context = new DefaultHttpContext();
        if (suppliedKey is not null)
        {
            context.Request.Headers[HeaderName] = suppliedKey;
        }

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Items.TryGetValue("ApiKeyValid", out var result);
        return result;
    }

    [Fact]
    public async Task The_Correct_Key_Is_Valid()
    {
        var result = await Invoke(Middleware(), RealKey);

        result.Should().Be(true);
    }

    [Fact]
    public async Task A_Wrong_Key_Of_The_Same_Length_Is_Invalid()
    {
        var result = await Invoke(Middleware(), "wrong-key-6789zz");

        result.Should().Be(false);
    }

    [Fact]
    public async Task A_Wrong_Key_That_Shares_A_Long_Correct_Prefix_Is_Invalid()
    {
        // Under the old StringValues == string comparison, this is the case whose response time
        // differed most from a totally-wrong guess - it differs only in the last character.
        var result = await Invoke(Middleware(), "correct-key-12346");

        result.Should().Be(false);
    }

    [Fact]
    public async Task A_Shorter_Wrong_Key_Is_Invalid()
    {
        var result = await Invoke(Middleware(), "correct-key");

        result.Should().Be(false);
    }

    [Fact]
    public async Task A_Missing_Header_Is_Invalid()
    {
        var result = await Invoke(Middleware(), null);

        result.Should().Be(false);
    }

    [Fact]
    public async Task No_Configured_Header_Name_Is_Invalid()
    {
        var result = await Invoke(Middleware(headerName: null), RealKey);

        result.Should().Be(false);
    }

    [Fact]
    public async Task No_Configured_Key_Is_Invalid_Even_For_An_Empty_Supplied_Key()
    {
        var result = await Invoke(Middleware(configuredKey: null), "");

        result.Should().Be(false);
    }
}
