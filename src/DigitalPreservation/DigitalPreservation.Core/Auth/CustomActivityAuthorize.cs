using System.CodeDom.Compiler;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DigitalPreservation.Core.Auth;

public sealed class CustomActivityAuthorize : Attribute, IAuthorizationFilter
{
    private readonly string apiKeyHeaderName = AuthConstants.ActivityApiKeyHeader;
  
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        //Check if machine token and create new claims identity
        if (context.HttpContext.Request.Headers.TryGetValue(apiKeyHeaderName, out var keyValue))
        {
           
            if (ToptHelper.Verify(keyValue))
                return;
        }

        //Must be un unauthorised
        context.Result = new UnauthorizedObjectResult("Unauthorized");
    }
}
