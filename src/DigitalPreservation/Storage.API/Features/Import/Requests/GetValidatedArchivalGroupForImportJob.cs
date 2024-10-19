using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public class GetValidatedArchivalGroupForImportJob(string pathUnderFedoraRoot, Transaction? transaction = null) : IRequest<Result<ArchivalGroup?>>
{
    public string PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public Transaction? Transaction { get; } = transaction;
}

public class GetValidatedArchivalGroupForImportJobHandler(IFedoraClient fedoraClient) : IRequestHandler<GetValidatedArchivalGroupForImportJob, Result<ArchivalGroup?>>
{
    public async Task<Result<ArchivalGroup?>> Handle(GetValidatedArchivalGroupForImportJob request, CancellationToken cancellationToken)
    {
        var info = await fedoraClient.GetResourceType(request.PathUnderFedoraRoot, request.Transaction);
        if (info is { Success: true, Value: nameof(ArchivalGroup) })
        {
            var ag = await fedoraClient.GetPopulatedArchivalGroup(request.PathUnderFedoraRoot, null, request.Transaction);
            return ag;
        }
        if (info.ErrorCode == ErrorCodes.NotFound)
        {
            var validateResult = await fedoraClient.ContainerCanBeCreatedAtPath(request.PathUnderFedoraRoot, request.Transaction);
            if (validateResult.Failure)
            {
                return Result.Cast<Container?, ArchivalGroup?>(validateResult);
            }
            return Result.Ok<ArchivalGroup>(null);
        }
        return Result.Fail<ArchivalGroup?>(info.ErrorCode ?? ErrorCodes.UnknownError,
            $"Cannot create Archival Group {request.PathUnderFedoraRoot} - {info.ErrorMessage}");
    }
}