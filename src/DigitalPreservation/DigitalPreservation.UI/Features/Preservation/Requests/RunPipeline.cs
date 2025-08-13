using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage.Ocfl;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class RunPipeline(Deposit deposit, string runUser) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
    public string? RunUser { get; set; } = runUser;

}

public class RunPipelineHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<RunPipeline, Result>
{
    public async Task<Result> Handle(RunPipeline request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.RunPipeline(request.Deposit, request.RunUser, cancellationToken);
    }

}