using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class RunPipeline(Deposit deposit) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
}

public class RunPipelineHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<RunPipeline, Result>
{
    public async Task<Result> Handle(RunPipeline request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.RunPipeline(request.Deposit, cancellationToken);
    }

}