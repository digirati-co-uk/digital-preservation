using DigitalPreservation.Common.Model;
using MediatR;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Repository.Requests;

/// <summary>
/// 
/// </summary>
/// <param name="pathUnderFedoraRoot">The Fedora sub path, not including any fedora or storage URI prefix</param>
public class GetResourceFromFedora(string? pathUnderFedoraRoot) : IRequest<PreservedResource?>
{
    public string? PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
}

public class GetResourceFromFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<GetResourceFromFedora, PreservedResource?>
{
    public async Task<PreservedResource?> Handle(GetResourceFromFedora request, CancellationToken cancellationToken)
    {
        return await fedoraClient.GetResource(request.PathUnderFedoraRoot, cancellationToken: cancellationToken);
    }
}