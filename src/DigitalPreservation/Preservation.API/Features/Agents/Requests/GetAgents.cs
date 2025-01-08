using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Agents.Requests;

public class GetAgents : IRequest<Result<List<Uri>>>;

public class GetAgentsHandler(
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetAgents, Result<List<Uri>>>
{
    public async Task<Result<List<Uri>>> Handle(GetAgents request, CancellationToken cancellationToken)
    {
        var agentStrings = 
                   dbContext.Deposits.Select(d => d.CreatedBy).Distinct()
            .Union(dbContext.Deposits.Select(d => d.LastModifiedBy).Distinct())
            .Union(dbContext.Deposits.Select(d => d.PreservedBy).Distinct())
            .Union(dbContext.Deposits.Select(d => d.ExportedBy).Distinct())
            .Where(s => s != null);
        List<Uri> agentUris = (await agentStrings
            .Select(a => resourceMutator.GetAgentUri(a))
            .ToListAsync(cancellationToken))!;
        return Result.OkNotNull(agentUris);
    }
}