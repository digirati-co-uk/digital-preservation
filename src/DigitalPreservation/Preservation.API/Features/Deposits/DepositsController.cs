using System.Text.Json;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using LeedsDlipServices.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;

namespace Preservation.API.Features.Deposits;


[Route("[controller]")]
[ApiController]
public class DepositsController(
    ILogger<DepositsController> logger,
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory
    ) : Controller
{
    [HttpGet(Name = "ListDeposits")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ListDeposits([FromQuery] DepositQuery? query) // 
    {
        var result = await mediator.Send(new GetDeposits(query));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpGet("{id}", Name = "GetDeposit")]
    [ProducesResponseType<Deposit>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new GetDeposit(id));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpGet("{id}/mets", Name = "GetDepositWithMets")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDepositMets([FromRoute] string id)
    {
        var wrapper = await mediator.Send(new GetDepositWithMets(id));
        if (wrapper is
            {
                Success: true, 
                Value: not null, 
                Value.MetsFileWrapper: not null, 
                Value.MetsFileWrapper.XDocument: not null
            })
        {
            Response.Headers.ETag = wrapper.Value.MetsFileWrapper.ETag;
            return Content(wrapper.Value.MetsFileWrapper.XDocument!.ToString(), "application/xml");
        }
        return this.StatusResponseFromResult(wrapper);
    }



    [HttpPost("{id}/mets", Name = "AddDepositItemsToMets")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> AddItemsToMets([FromRoute] string id, [FromBody] List<string> localPaths)
    {
        // we can't use default serialisation here
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is not { Success: true, Value: not null })
        {
            return this.StatusResponseFromResult(depositResult);
        }
        var deposit = depositResult.Value;
        if (deposit.MetsETag != null) // only should true when there is no METS file
        {
            var eTag = Request.Headers.IfMatch.FirstOrDefault();
            if (!eTag.HasText() || eTag != deposit.MetsETag)
            {
                logger.LogWarning("Supplied eTag {eTag} does not match deposit eTag {depositETag}", eTag, deposit.MetsETag);
                var pd = new ProblemDetails
                {
                    Title = "Conflict: ETag does not match deposit METS",
                    Detail = deposit.MetsETag,
                    Status = 409
                };
                return Conflict(pd);
            }
        }

        var workspaceManager = await workspaceManagerFactory.CreateAsync(depositResult.Value);
        var filesystemResult = await workspaceManager.GetFileSystemWorkingDirectory(refresh: true);
        if (filesystemResult is not { Success: true, Value: not null })
        {
            return this.StatusResponseFromResult(filesystemResult);
        }
        var fileSystem = filesystemResult.Value;
        var wbsToAdd = new List<WorkingBase>();
        foreach (var path in localPaths)
        {
            // we don't know if it's a file or a directory from just the path,
            // but it's not expensive now to find out
            WorkingBase? wbToAdd = fileSystem.FindFile(path);
            if (wbToAdd is null)
            {
                wbToAdd = fileSystem.FindDirectory(path);
            }

            if (wbToAdd != null)
            {
                wbsToAdd.Add(wbToAdd);
            }
        }

        var result = await workspaceManager.AddItemsToMets(wbsToAdd, User.GetCallerIdentity());
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpPost("{id}/mets/delete", Name = "DeleteFromDeposit")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> DeleteItemsToMets([FromRoute] string id, [FromBody] DeleteSelection deleteSelection)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is { Success: true, Value: not null })
        {
            var deposit = depositResult.Value;
            var eTag = Request.Headers.IfMatch.FirstOrDefault();
            if (eTag.HasText() && eTag == deposit.MetsETag)
            {
                var workspaceManager = await workspaceManagerFactory.CreateAsync(depositResult.Value);
                var deleteResult = await workspaceManager.DeleteItems(deleteSelection, User.GetCallerIdentity());
                return this.StatusResponseFromResult(deleteResult);
            }

            var pd = new ProblemDetails
            {
                Title = "Conflict: ETag does not match deposit METS",
                Detail = deposit.MetsETag,
                Status = 409
            };
            return Conflict(pd);
        }
        return this.StatusResponseFromResult(depositResult);
    }
    
    [HttpGet("{id}/filesystem", Name = "GetWorkingDirectory")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetFileSystem([FromRoute] string id, [FromQuery] bool refresh = false)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is { Success: true, Value: not null })
        {
            var workspaceManager = await workspaceManagerFactory.CreateAsync(depositResult.Value);
            var workingDirectoryResult = await workspaceManager.GetFileSystemWorkingDirectory(refresh);
            return this.StatusResponseFromResult(workingDirectoryResult);
        }
        return this.StatusResponseFromResult(depositResult);
    }
    
    /// <summary>
    /// It is not a good idea to request this!
    /// It contains a huge number of self-references across the combined trees, which
    /// when serialised produce way too much JSON!
    /// Use for debugging only.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="refresh"></param>
    /// <returns></returns>
    [HttpGet("{id}/combined", Name = "GetCombinedDirectory")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetCombinedDirectory([FromRoute] string id, [FromQuery] bool refresh = false)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is { Success: true, Value: not null })
        {
            var workspaceManager = await workspaceManagerFactory.CreateAsync(depositResult.Value, refresh);
            var result = workspaceManager.GetRootCombinedDirectory();
            return this.StatusResponseFromResult(result);
        }
        return this.StatusResponseFromResult(depositResult);
    }
        
    [HttpPatch("{id}", Name = "PatchDeposit")]
    [ProducesResponseType<Deposit>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> PatchDeposit([FromRoute] string id, [FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new GetDeposit(id));
        if (result is { Success: true, Value: not null })
        {
            var existingDeposit = result.Value;
            deposit.Id = existingDeposit.Id;
            var patchResult = await mediator.Send(new PatchDeposit(deposit, User));
            return this.StatusResponseFromResult(patchResult);
        }
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpDelete("{id}", Name = "DeleteDeposit")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> DeleteDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new DeleteDeposit(id, User));
        return this.StatusResponseFromResult(result, 204);
    }
    
    
    [HttpPost(Name = "CreateDeposit")]
    [ProducesResponseType<Deposit>(201, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateDeposit([FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new CreateDeposit(deposit, false, User));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    [HttpPost("from-identifier", Name = "CreateDepositFromIdentifier")]
    [ProducesResponseType<Deposit>(201, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateDepositFromIdentifier([FromBody] SchemaAndValue schemaAndValue)
    {
        var result = await mediator.Send(new CreateDepositFromIdentifier(schemaAndValue, User));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    [HttpPost("export", Name = "Export")]
    [ProducesResponseType<Deposit>(201, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ExportArchivalGroup([FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new CreateDeposit(deposit, true, User));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    [HttpPost("{id}/lock", Name = "CreateLock")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateLock([FromRoute] string id, [FromQuery] bool force = false)
    {
        var lockDepositResult = await mediator.Send(new LockDeposit(id, force, User));
        return this.StatusResponseFromResult(lockDepositResult, successStatusCode: 204);
    }
    
    
    [HttpDelete("{id}/lock", Name = "DeleteLock")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> DeleteLock([FromRoute] string id)
    {
        var deleteDepositLockResult = await mediator.Send(new DeleteDepositLock(id, User));
        return this.StatusResponseFromResult(deleteDepositLockResult, successStatusCode: 204);
    }


    [HttpPost("{id}/pipeline", Name = "RunPipeline")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RunPipeline([FromRoute] string id, [FromQuery] string? runUser)
    {
        var runPipelineResult = await mediator.Send(new RunPipeline(id, User, runUser));
        return this.StatusResponseFromResult(runPipelineResult, successStatusCode: 204);
    }


    [HttpPost("pipeline-status", Name = "LogPipelineRunStatus")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> LogPipelineRunStatus([FromBody] PipelineDeposit pipelineDeposit)
    {
        var runPipelineStatusResult = await mediator.Send(new RunPipelineStatus(pipelineDeposit.Id, pipelineDeposit.DepositId , pipelineDeposit.Status ?? string.Empty, User, pipelineDeposit.RunUser, pipelineDeposit.Errors)); 

        return this.StatusResponseFromResult(runPipelineStatusResult, successStatusCode: 204);
    }

}