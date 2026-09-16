using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Storage.API.Web;

namespace Storage.API.Tests.Web;

/// <summary>
/// The filter must not depend on what a controller happens to call its route parameter: a future
/// {*resourcePath} is covered the day it is written. Routing's own values are left alone.
/// </summary>
public class RepositoryPathFilterUnitTests
{
    [Theory]
    [InlineData("path", "%2e%2e/x")]
    [InlineData("archivalGroupPathUnderRoot", "//evil.com/x")]
    [InlineData("resourcePath", "../x")]          // a name the filter has never heard of
    [InlineData("anything", "cc/thing/fcr:tombstone")]
    public void A_Bad_Path_Is_Refused_Whatever_The_Route_Value_Is_Called(string name, string value)
    {
        var context = Context(new RouteValueDictionary { ["controller"] = "Repository", ["action"] = "Browse", [name] = value });

        new RepositoryPathFilter().OnActionExecuting(context);

        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Ordinary_Route_Values_Pass()
    {
        var context = Context(new RouteValueDictionary
        {
            ["controller"] = "Import", ["action"] = "ImportJobResult",
            ["jobIdentifier"] = "abc123def456", ["archivalGroupPathUnderRoot"] = "cc/thing/"
        });

        new RepositoryPathFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Routing_Values_Are_Not_Judged()
    {
        // "controller" and "action" are routing's own, and a Razor "page" value has slashes in it.
        var context = Context(new RouteValueDictionary { ["controller"] = "..", ["action"] = "//x", ["page"] = "/Deposits/Index" });

        new RepositoryPathFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    private static ActionExecutingContext Context(RouteValueDictionary routeValues)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(routeValues), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);
    }
}
