using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class GetResourceTypeFromFedora(string? pathUnderFedoraRoot) : IRequest<Result<string?>>
{
    public string? PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
}

public class GetResourceTypeFromFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<GetResourceTypeFromFedora, Result<string?>>
{
    public async Task<Result<string?>> Handle(GetResourceTypeFromFedora request, CancellationToken cancellationToken)
    {
        return await fedoraClient.GetResourceType(request.PathUnderFedoraRoot);
    }
}