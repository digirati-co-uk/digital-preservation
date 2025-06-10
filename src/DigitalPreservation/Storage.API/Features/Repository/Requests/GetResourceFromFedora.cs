using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Repository.Requests;

/// <summary>
/// 
/// </summary>
/// <param name="pathUnderFedoraRoot">The Fedora sub path, not including any fedora or storage URI prefix</param>
public class GetResourceFromFedora(string? pathUnderFedoraRoot, int page = 1, int pageSize = GetResourceFromFedora.DefaultPageSize) : IRequest<Result<PreservedResource?>>
{
    public const int DefaultPageSize = 100;
    public string? PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
}

public class GetResourceFromFedoraHandler(IFedoraClient fedoraClient) : IRequestHandler<GetResourceFromFedora, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetResourceFromFedora request, CancellationToken cancellationToken)
    {
        return await fedoraClient.GetResource(request.PathUnderFedoraRoot, request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}