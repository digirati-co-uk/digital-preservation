using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using LinqKit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Features.Deposits.Requests;

public class GetDeposits(DepositQuery? query) : IRequest<Result<List<Deposit>>>
{
    public DepositQuery? Query  { get; } = query;
}

public class GetDepositsHandler(
    ILogger<GetDepositsHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetDeposits, Result<List<Deposit>>>
{
    public async Task<Result<List<Deposit>>> Handle(GetDeposits request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Query is null || request.Query.NoTerms())
            {
                var noQueryEntities = dbContext.Deposits
                    .AsQueryable().OrderByDescending(d => d.Created).Take(500);
                return Result.OkNotNull(resourceMutator.MutateDeposits(noQueryEntities));
            }
            var q = request.Query;
            var predicate = PredicateBuilder.New<DepositEntity>();
            if (q.ArchivalGroupPath.HasText())
            {
                predicate = predicate.And(x => x.ArchivalGroupPathUnderRoot == q.ArchivalGroupPath.GetPathUnderRoot());
            }

            if (q.CreatedAfter.HasValue)
            {
                predicate = predicate.And(x => x.Created >= q.CreatedAfter);
            }

            if (q.CreatedBefore.HasValue)
            {
                predicate = predicate.And(x => x.Created < q.CreatedBefore);
            }

            if (q.CreatedBy != null)
            {
                predicate = predicate.And(x => x.CreatedBy == q.CreatedBy.ToString().GetSlug());
            }

            if (q.LastModifiedAfter.HasValue)
            {
                predicate = predicate.And(x => x.LastModified >= q.LastModifiedAfter);
            }

            if (q.LastModifiedBefore.HasValue)
            {
                predicate = predicate.And(x => x.LastModified < q.LastModifiedBefore);
            }

            if (q.LastModifiedBy != null)
            {
                predicate = predicate.And(x => x.LastModifiedBy == q.LastModifiedBy.ToString().GetSlug());
            }

            if (q.PreservedAfter.HasValue)
            {
                predicate = predicate.And(x => x.Preserved >= q.PreservedAfter);
            }

            if (q.PreservedBefore.HasValue)
            {
                predicate = predicate.And(x => x.Preserved < q.PreservedBefore);
            }

            if (q.PreservedBy != null)
            {
                predicate = predicate.And(x => x.PreservedBy == q.PreservedBy.ToString().GetSlug());
            }

            if (q.ExportedAfter.HasValue)
            {
                predicate = predicate.And(x => x.Exported >= q.ExportedAfter);
            }

            if (q.ExportedBefore.HasValue)
            {
                predicate = predicate.And(x => x.Exported < q.ExportedBefore);
            }

            if (q.ExportedBy != null)
            {
                predicate = predicate.And(x => x.ExportedBy == q.ExportedBy.ToString().GetSlug());
            }

            if (q.Active)
            {
                // We never _exclude_ active, but sometimes we only want active.
                predicate = predicate.And(x => x.Active == q.Active);
            }

            if (q.Status.HasText())
            {
                predicate = predicate.And(x => x.Status == q.Status);
            }

            var entities = dbContext.Deposits.Where(predicate);
            switch (q.OrderBy)
            {
                case nameof(Deposit.CreatedBy):
                    entities = (q.Ascending ?? false)
                        ? entities.OrderByDescending(x => x.CreatedBy)
                        : entities.OrderBy(x => x.CreatedBy);
                    break;
                case nameof(Deposit.Created):
                default:
                    entities = (q.Ascending ?? false)
                        ? entities.OrderByDescending(x => x.Created)
                        : entities.OrderBy(x => x.Created);
                    break;
            }
            // TODO: more orderBys
            
            var result = await entities.Take(500).ToListAsync(cancellationToken: cancellationToken);
            var deposits = resourceMutator.MutateDeposits(result);
            return Result.OkNotNull(deposits);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<Deposit>>(ErrorCodes.UnknownError, e.Message);
        }
    }
}