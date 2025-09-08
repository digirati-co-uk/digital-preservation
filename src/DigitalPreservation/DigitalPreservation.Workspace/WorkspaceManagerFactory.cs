using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using MediatR;

namespace DigitalPreservation.Workspace;

public class WorkspaceManagerFactory(IMediator mediator, IMetsParser metsParser)
{
    public async Task<WorkspaceManager> CreateAsync(Deposit deposit, bool refresh = false)
    {
        var workspaceManager = new WorkspaceManager(deposit, mediator, metsParser);
        await workspaceManager.InitialiseAsync(refresh);
        return workspaceManager;
    }
}