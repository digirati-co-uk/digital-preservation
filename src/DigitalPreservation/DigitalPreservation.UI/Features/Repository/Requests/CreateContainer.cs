using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class CreateContainer(string? pathUnderRoot, string containerSlug, string? containerTitle) : IRequest<Result<Container?>>
{
    public string? PathUnderRoot { get; } = pathUnderRoot;
    public string Slug { get; } = containerSlug;
    public string? Title { get; } = containerTitle;
}

public class CreateContainerHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<CreateContainer, Result<Container?>>
{
    public async Task<Result<Container?>> Handle(CreateContainer request, CancellationToken cancellationToken)
    {
        var newPath = StringUtils.BuildPath(false, 
            PreservedResource.BasePathElement, request.PathUnderRoot, request.Slug);
        var result = await preservationApiClient.CreateContainer(newPath, request.Title);
        return result;
    }
}