using DigitalPreservation.Common.Model.Results;
using MediatR;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class GetExportResult(string identifier) : IRequest<Result<ExportResource?>>
{
    public string Identifier { get; } = identifier;
}

public class GetExportResultHandler(IExportResultStore exportResultStore) : IRequestHandler<GetExportResult, Result<ExportResource?>>
{
    public async Task<Result<ExportResource?>> Handle(GetExportResult request, CancellationToken cancellationToken)
    {
        var result = await exportResultStore.GetExportResult(request.Identifier, cancellationToken);
        return result;
    }
}