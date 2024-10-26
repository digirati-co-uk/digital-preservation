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
        var result = await fedoraClient.GetValidatedArchivalGroupForImportJob(request.PathUnderFedoraRoot, request.Transaction);
        return result;
    }
}