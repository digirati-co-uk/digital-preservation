using DigitalPreservation.Common.Model;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class GetResource(string path) : IRequest<PreservedResource?>
{
    public string Path { get; set; } = path;
}

public class GetResourceHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetResource, PreservedResource?>
{
    public async Task<PreservedResource?> Handle(GetResource request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetResource(request.Path);
    }
}