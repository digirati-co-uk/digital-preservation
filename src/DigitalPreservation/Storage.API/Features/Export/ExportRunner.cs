using MediatR;
using Storage.API.Features.Export.Requests;

namespace Storage.API.Features.Export;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IMediator mediator,
    IExportResultStore exportResultStore)
{
        
    public async Task Execute(string identifier, CancellationToken cancellationToken)
    {
        var exportResult = await exportResultStore.GetExportResult(identifier, cancellationToken);
        if (exportResult is { Success: true, Value: not null })
        {
            var executeResult = await mediator.Send(new ExecuteExport(identifier, exportResult.Value), cancellationToken);
            if (executeResult.Success)
            {
                logger.LogInformation($"Export executed for {identifier}");
                return;
            }
            logger.LogError("Unable to execute Export: " + executeResult.CodeAndMessage());
            return;
        }
        logger.LogError("Unable to load Export: " + exportResult.CodeAndMessage());
    }
}