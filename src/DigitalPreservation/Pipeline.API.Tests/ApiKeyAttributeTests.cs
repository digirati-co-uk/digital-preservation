using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pipeline.API.Middleware;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// ApiKeyAttribute.OnAuthorization used to return early - treating the request as authorized -
/// when the "ApiKeyValid" HttpContext.Items entry was absent, on the theory that this could only
/// happen if ApiKeyMiddleware was not registered. Program.cs always registers it, so a missing
/// entry should fail closed like an explicit false, not open (issue #275 item 2).
/// </summary>
public class ApiKeyAttributeTests
{
    private static AuthorizationFilterContext BuildContext(bool? apiKeyValid)
    {
        var httpContext = new DefaultHttpContext();
        if (apiKeyValid.HasValue)
        {
            httpContext.Items["ApiKeyValid"] = apiKeyValid.Value;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public void NoApiKeyValidItem_IsUnauthorized()
    {
        var context = BuildContext(null);

        new ApiKeyAttribute().OnAuthorization(context);

        var result = Assert.IsType<ContentResult>(context.Result);
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ApiKeyValidFalse_IsUnauthorized()
    {
        var context = BuildContext(false);

        new ApiKeyAttribute().OnAuthorization(context);

        var result = Assert.IsType<ContentResult>(context.Result);
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ApiKeyValidTrue_IsAuthorized()
    {
        var context = BuildContext(true);

        new ApiKeyAttribute().OnAuthorization(context);

        context.Result.Should().BeNull();
    }
}
