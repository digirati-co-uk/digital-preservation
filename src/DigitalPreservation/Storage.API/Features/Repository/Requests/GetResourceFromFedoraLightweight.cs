using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class GetResourceFromFedoraLightweight(string? pathUnderFedoraRoot, string? version) : IRequest<Result<PreservedResource?>>
{
    public string? PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public string? Version { get; } = version;
}

public class GetResourceFromFedoraLightweightHandler(
    IFedoraClient fedoraClient) : IRequestHandler<GetResourceFromFedoraLightweight, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetResourceFromFedoraLightweight request, CancellationToken cancellationToken)
    {
        var result = await fedoraClient.GetResourceLightweight(request.PathUnderFedoraRoot, request.Version);
        return result;
    }
}
