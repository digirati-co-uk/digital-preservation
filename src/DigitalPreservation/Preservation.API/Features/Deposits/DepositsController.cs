using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Preservation.API.Features.Deposits;


[Route(PreservedResource.BasePathElement + "/{*path}")]
[ApiController]
public class DepositsController(IMediator mediator) : Controller
{
    
}