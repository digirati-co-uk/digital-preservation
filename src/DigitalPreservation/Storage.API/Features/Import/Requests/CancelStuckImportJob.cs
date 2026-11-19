using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;

namespace Storage.API.Features.Import.Requests;

/// <summary>
/// Operator path for issue #366 - see <see cref="IImportJobResultStore.CancelStuckImportJob"/> for
/// the full rationale. Replaces the raw <c>update import_jobs set active = false where id = …</c>
/// SQL that was the only remedy before this existed (issue #245).
/// </summary>
public class CancelStuckImportJob(string jobIdentifier, string reason) : IRequest<Result<ImportJobResult>>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string Reason { get; } = reason;
}

public class CancelStuckImportJobHandler(IImportJobResultStore importJobResultStore)
    : IRequestHandler<CancelStuckImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(CancelStuckImportJob request, CancellationToken cancellationToken)
    {
        // Required so the record this leaves behind explains itself - the whole point of preferring
        // this over a silent row update (issue #245's "the audit trail is better than a row update").
        if (request.Reason.IsNullOrWhiteSpace())
        {
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, "A reason is required to cancel an import job");
        }

        return await importJobResultStore.CancelStuckImportJob(request.JobIdentifier, request.Reason, cancellationToken);
    }
}
