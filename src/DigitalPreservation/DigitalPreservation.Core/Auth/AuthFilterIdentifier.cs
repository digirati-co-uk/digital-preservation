using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

public sealed class AuthFilterIdentifier : IAsyncAuthorizationFilter
{
    //This used by PropagateCorrelationIdHandler
    public const string MachineHeaderName = "xMachineName";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        //return if valid standard user user
        if (!string.IsNullOrEmpty(context.HttpContext.User.GetDisplayName()))
        {
            return Task.CompletedTask;
        }

        //Check if machine token and create new claims identity
        if (context.HttpContext.Request.Headers.TryGetValue(MachineHeaderName, out var machineName))
        {
            ////Set claims  
            var claims = new List<Claim>
            {
                new Claim("Name", machineName.FirstOrDefault() ?? string.Empty)
            };
            context.HttpContext.User.AddIdentity(new ClaimsIdentity(claims));

            return Task.CompletedTask;
        }

        //Must be un unauthorised
        context.Result = new UnauthorizedObjectResult("Unauthorized: No valid user name or machine header");
        return Task.CompletedTask;
    }
}
