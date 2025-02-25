using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Workspace;

public class WorkspaceManagerFactory(
    ILogger<WorkspaceManager> logger,
    IMediator mediator,
    IStorage storage,
    IMetsParser metsParser,
    IMetsManager metsManager)
{

    public WorkspaceManager Create(Deposit deposit)
    {
        return new WorkspaceManager(deposit, logger, mediator, storage, metsParser, metsManager);
    }
    
}