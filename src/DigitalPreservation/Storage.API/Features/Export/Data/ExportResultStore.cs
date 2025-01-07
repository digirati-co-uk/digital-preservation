using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Microsoft.EntityFrameworkCore;
using Storage.API.Data;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Data;

public class ExportResultStore(
    StorageContext dbContext,
    ILogger<ExportResultStore> logger) : IExportResultStore
{
    public async Task<Result<ExportResource?>> GetExportResult(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.ExportResults.AsNoTracking().SingleOrDefaultAsync(er => er.Id == identifier, cancellationToken);
            if (entity == null)
            {
                return Result.Fail<ExportResource?>(ErrorCodes.NotFound, $"Export {identifier} could not be found");
            }
            var export = JsonSerializer.Deserialize<ExportResource?>(entity.ExportResultJson!);
            if (export == null)
            {
                return Result.Fail<ExportResource>(ErrorCodes.UnknownError, $"Export {identifier} has no JSON body in storage.");
            }
            return Result.Ok(export);
        }
        catch (Exception e)
        {
            return Result.Fail<ExportResource>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> CreateExportResult(
        string identifier,
        ExportResource export,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.ExportResults.AddAsync(
                new API.Data.Entities.Export
                {
                    Id = identifier, 
                    ArchivalGroup = export.ArchivalGroup,
                    ExportResultJson = JsonSerializer.Serialize(export),
                    Destination = export.Destination
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

    public async Task<Result> UpdateExportResult(
        string identifier, 
        ExportResource export,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.ExportResults.FindAsync([identifier], cancellationToken);
            if (entity == null)
            {
                return Result.Fail(ErrorCodes.NotFound, $"Export {identifier} could not be found");
            }
            entity.ExportResultJson = JsonSerializer.Serialize(export);
            if (export.DateBegun.HasValue)
            {
                entity.DateBegun = export.DateBegun.Value;
            }
            if (export.DateFinished.HasValue)
            {
                entity.DateFinished = export.DateFinished.Value;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<List<string>>> GetUnfinishedExportsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken)
    {
        try
        {
            var jobIds = await dbContext.ExportResults
                .Where(er => er.ArchivalGroup == archivalGroup && er.DateFinished == null)
                .Select(er => er.Id)
                .ToListAsync(cancellationToken);
            return Result.OkNotNull(jobIds);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<string>>(ErrorCodes.UnknownError, e.Message);
        }
    }
}