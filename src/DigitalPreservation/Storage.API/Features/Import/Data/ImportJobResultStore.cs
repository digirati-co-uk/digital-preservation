using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using Microsoft.EntityFrameworkCore;
using Storage.API.Data;

namespace Storage.API.Features.Import.Data;

public class ImportJobResultStore(
    StorageContext dbContext,
    ILogger<ImportJobResultStore> logger) : IImportJobResultStore
{
    public async Task<Result<List<string>>> GetActiveJobsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken)
    {
        try
        {
            var jobIds = await dbContext.ImportJobs
                .Where(ij => ij.ArchivalGroup == archivalGroup && ij.Active == true)
                .Select(ij => ij.Id)
                .ToListAsync(cancellationToken);
            return Result.OkNotNull(jobIds);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<string>>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result> SaveImportJob(string jobIdentifier, ImportJob importJob, CancellationToken cancellationToken = default)
    {
        if (importJob.ArchivalGroup == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Ingest Job must specify an archival group");
        }

        try
        {
            await dbContext.ImportJobs.AddAsync(
                new API.Data.Entities.ImportJob
                {
                    Id = jobIdentifier, 
                    ArchivalGroup = importJob.ArchivalGroup,
                    ImportJobJson = JsonSerializer.Serialize(importJob),
                    Active = true
                }, 
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> SaveImportJobResult(
        string jobIdentifier, 
        ImportJobResult importJobResult, 
        bool active, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await dbContext.ImportJobs.FindAsync([jobIdentifier], cancellationToken);
            if (entity == null)
            {
                return Result.Fail(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            entity.ImportJobResultJson = JsonSerializer.Serialize(importJobResult);
            entity.Active = active;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }


    public async Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.ImportJobs.AsNoTracking().SingleOrDefaultAsync(ij => ij.Id == jobIdentifier, cancellationToken);
            if (entity == null)
            {
                return Result.Fail<ImportJob>(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            var importJob = JsonSerializer.Deserialize<ImportJob>(entity.ImportJobJson);
            if (importJob == null)
            {
                return Result.Fail<ImportJob>(ErrorCodes.UnknownError, $"Import job {jobIdentifier} has no JSON body in storage.");
            }
            return Result.Ok(importJob);
        }
        catch (Exception e)
        {
            return Result.Fail<ImportJob>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken)
    {        
        try
        {
            var entity = await dbContext.ImportJobs.AsNoTracking().SingleOrDefaultAsync(ij => ij.Id == jobIdentifier, cancellationToken);
            if (entity == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            if (entity.ImportJobResultJson == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, $"Result for Import Job {jobIdentifier} has no JSON body in storage.");
            }
            var importJobResult = JsonSerializer.Deserialize<ImportJobResult>(entity.ImportJobResultJson);
            if (importJobResult == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, $"Result for Import Job {jobIdentifier} could not be deserialised.");
            }
            return Result.Ok(importJobResult);
        }
        catch (Exception e)
        {
            return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }
}