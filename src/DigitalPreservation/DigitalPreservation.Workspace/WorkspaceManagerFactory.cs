using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using MediatR;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace;

public class WorkspaceManagerFactory(IMediator mediator, IMetsParser metsParser)
{
    public WorkspaceManager Create(Deposit deposit)
    {
        return new WorkspaceManager(deposit, mediator, metsParser);
    }
    
}