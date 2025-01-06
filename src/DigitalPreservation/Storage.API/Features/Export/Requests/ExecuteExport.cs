using DigitalPreservation.Common.Model.Results;
using MediatR;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class ExecuteExport(string identifier, ExportResource export) : IRequest<Result<ExportResource>>
{
    public string Identifier { get; } = identifier;
    public ExportResource Export { get; } = export;
}

public class ExecuteExportHandler(
    IExportResultStore exportResultStore,
    ILogger<ExecuteExportHandler> logger) : IRequestHandler<ExecuteExport, Result<ExportResource>>
{
    public async Task<Result<ExportResource>> Handle(ExecuteExport request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}