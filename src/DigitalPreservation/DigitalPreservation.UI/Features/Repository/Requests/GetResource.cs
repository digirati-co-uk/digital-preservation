using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class GetResource(string path) : IRequest<Result<PreservedResource?>>
{
    public string Path { get; set; } = path;
}

public class GetResourceHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetResource, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetResource request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetResource(request.Path);
    }
}