using Microsoft.AspNetCore.Mvc.Filters;

namespace DigitalPreservation.UI.Infrastructure;

public class SessionTimeoutAsyncPageFilter(Logger<SessionTimeoutAsyncPageFilter> logger) : IAsyncPageFilter
{
    
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) =>
    Task.CompletedTask;
    

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        try
        {
            var session = context.HttpContext.Session;
            if (!session.Keys.Any())
            {
                //try and refresh the request path if possible
                context.HttpContext.Response.Redirect($"/Account/RefreshLogin?path={context.HttpContext.Request.Path}");
                return;
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Session invalid, redirecting to RefreshLogin");
            context.HttpContext.Response.Redirect("/Account/RefreshLogin");
            return;
        }
        await next.Invoke();
    }

}