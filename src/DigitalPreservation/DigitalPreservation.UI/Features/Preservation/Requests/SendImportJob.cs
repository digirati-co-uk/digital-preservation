using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class SendImportJob(ImportJob importJob) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
}

public class SendImportJobHandler : IRequestHandler<SendImportJob, Result<ImportJobResult>>
{
    public Task<Result<ImportJobResult>> Handle(SendImportJob request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}