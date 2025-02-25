using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace DigitalPreservation.UI.Features.Workspace;

public class GetMetsWrapper(Uri metsFileLocation) : IRequest<Result<MetsFileWrapper>>
{
    public Uri MetsFileLocation { get; } = metsFileLocation;
}

public class MetsFileWrapperHandler(IMetsParser metsParser) : IRequestHandler<GetMetsWrapper, Result<MetsFileWrapper>>
{
    public async Task<Result<MetsFileWrapper>> Handle(GetMetsWrapper request, CancellationToken cancellationToken)
    {
        var wrapperResult = await metsParser.GetMetsFileWrapper(request.MetsFileLocation, true);
        return wrapperResult;
    }
}