using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class CreateContainer(string? pathUnderRoot, string containerSlug, string? containerTitle) : IRequest<Container?>
{
    public string? PathUnderRoot { get; } = pathUnderRoot;
    public string Slug { get; } = containerSlug;
    public string? Title { get; } = containerTitle;
}

public class CreateContainerHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<CreateContainer, Container?>
{
    public async Task<Container?> Handle(CreateContainer request, CancellationToken cancellationToken)
    {
        var newPath = StringUtils.BuildPath(false, 
            PreservedResource.BasePathElement, request.PathUnderRoot, request.Slug);
        var container = await preservationApiClient.CreateContainer(newPath, request.Title);
        return container;
    }
}