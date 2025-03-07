using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class TestArchivalGroupPath(string archivalGroupPathUnderRoot) : IRequest<Result<ArchivalGroup?>>
{
    public string ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
}

public class TestArchivalGroupPathHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<TestArchivalGroupPath, Result<ArchivalGroup?>>
{
    public async Task<Result<ArchivalGroup?>> Handle(TestArchivalGroupPath request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.TestArchivalGroupPath(request.ArchivalGroupPathUnderRoot);
    }
}