using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class CreateDeposit(
    string? archivalGroupPathUnderRoot,
    string? archivalGroupProposedName,
    string? submissionText): IRequest<Result<Deposit?>>
{
    public string? ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
    public string? ArchivalGroupProposedName { get; } = archivalGroupProposedName;
    public string? SubmissionText { get; } = submissionText;
}

public class CreateDepositHandler(
    IPreservationApiClient preservationApiClient) : IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.CreateDeposit(
            request.ArchivalGroupPathUnderRoot.GetRepositoryPath(),
            request.ArchivalGroupProposedName,
            request.SubmissionText,
            cancellationToken);
        return result;
    }
}