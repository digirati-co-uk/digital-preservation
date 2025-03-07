using DigitalPreservation.Common.Model.Results;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export;

public interface IExportResultStore
{
    public Task<Result<ExportResource?>> GetExportResult(
        string identifier, 
        CancellationToken cancellationToken);

    public Task<Result> CreateExportResult(
        string identifier,
        ExportResource export,
        CancellationToken cancellationToken);

    public Task<Result> UpdateExportResult(
        string identifier,
        ExportResource export,
        CancellationToken cancellationToken);
    
    public Task<Result<List<string>>> GetUnfinishedExportsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken);
}