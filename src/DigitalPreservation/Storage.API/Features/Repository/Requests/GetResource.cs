using DigitalPreservation.Common.Model;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class GetResource(string path) : IRequest<PreservedResource?>
{
    public string Path { get; } = path;
}

public class GetResourceHandler(IFedoraClient fedoraClient) : IRequestHandler<GetResource, PreservedResource?>
{
    public async Task<PreservedResource?> Handle(GetResource request, CancellationToken cancellationToken)
    {
        var uri = UriMapper.GetFedoraRelativeUri(request.Path);
        return await fedoraClient.GetResource(uri);
    }
}