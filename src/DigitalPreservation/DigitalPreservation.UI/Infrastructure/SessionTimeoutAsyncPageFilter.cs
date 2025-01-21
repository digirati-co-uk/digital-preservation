using Microsoft.AspNetCore.Mvc.Filters;

namespace DigitalPreservation.UI.Infrastructure;

public class SessionTimeoutAsyncPageFilter : IAsyncPageFilter
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
            context.HttpContext.Response.Redirect("/Account/RefreshLogin");
            return;
        }
        await next.Invoke();
    }

}