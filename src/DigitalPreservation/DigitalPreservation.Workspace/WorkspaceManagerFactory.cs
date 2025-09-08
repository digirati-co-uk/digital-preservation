using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using MediatR;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace;

public class WorkspaceManagerFactory(IMediator mediator, IMetsParser metsParser)
{
    public async Task<WorkspaceManager> CreateAsync(Deposit deposit)
    {
        var workspaceManager = new WorkspaceManager(deposit, mediator, metsParser);
        await workspaceManager.InitialiseAsync();
        return workspaceManager;
    }
}