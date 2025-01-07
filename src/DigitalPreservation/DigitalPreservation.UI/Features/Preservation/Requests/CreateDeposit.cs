using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class CreateDeposit(
    string? archivalGroupPathUnderRoot,
    string? archivalGroupProposedName,
    string? submissionText,
    bool useObjectsTemplate,
    bool export,
    string? exportVersion): IRequest<Result<Deposit?>>
{
    public string? ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
    public string? ArchivalGroupProposedName { get; } = archivalGroupProposedName;
    public string? SubmissionText { get; } = submissionText;
    public bool UseObjectsTemplate { get; } = useObjectsTemplate;
    public bool Export { get; } = export;
    public string? ExportVersion { get; } = exportVersion;
}

public class CreateDepositHandler(
    IOptions<PreservationOptions> options,
    IPreservationApiClient preservationApiClient) : IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.CreateDeposit(
            request.ArchivalGroupPathUnderRoot.GetRepositoryPath(),
            request.ArchivalGroupProposedName,
            request.SubmissionText,
            request.UseObjectsTemplate,
            request.Export,
            request.ExportVersion,
            cancellationToken);
        return result;
    }
}