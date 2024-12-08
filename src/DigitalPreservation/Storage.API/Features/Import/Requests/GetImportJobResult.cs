using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Repository.Common;

namespace Storage.API.Features.Import.Requests;

public class GetImportJobResult(string jobIdentifier, string archivalGroupPathUnderRoot) : IRequest<Result<ImportJobResult?>>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
}

public class GetImportJobResultHandler(IImportJobResultStore importJobResultStore) : IRequestHandler<GetImportJobResult, Result<ImportJobResult?>>
{
    public async Task<Result<ImportJobResult?>> Handle(GetImportJobResult request, CancellationToken cancellationToken)
    {
        var result = await importJobResultStore.GetImportJobResult(request.JobIdentifier, cancellationToken);
        // At this point we could validate that it is a result for an import to request.ArchivalGroupPathUnderRoot
        return result;
    }
}
