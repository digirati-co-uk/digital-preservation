using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.PipelineRunJobs.Requests;

public class GetPipelineJobResult(string depositId, string pipelineJobId) : IRequest<Result<ImportJobResult>>
{
    public string DepositId { get; } = depositId;
    public string PipelineJobId { get; } = pipelineJobId;
}

public class GetPipelineJobResultHandler(
    ILogger<GetPipelineJobResultHandler> logger,
    PreservationContext dbContext) : IRequestHandler<GetPipelineJobResult, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(GetPipelineJobResult request, CancellationToken cancellationToken)
    {
        return await Task.FromResult<Result<ImportJobResult>>(null);
    }
}
