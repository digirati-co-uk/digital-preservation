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
            IQueryable<DepositEntity> queryable;
            
            if (request.Query is null || request.Query.NoTerms())
            {
                queryable = dbContext.Deposits
                    .Where(d => d.Active)
                    .AsQueryable();
                //return Result.OkNotNull(resourceMutator.MutateDeposits(noQueryEntities));
            }
            else
            {
                var q = request.Query;
                var predicate = PredicateBuilder.New<DepositEntity>();
                if (q.ArchivalGroupPath.HasText())
                {
                    predicate = predicate.And(x =>
                        x.ArchivalGroupPathUnderRoot == q.ArchivalGroupPath.GetPathUnderRoot(false));
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

                if (q.ShowAll is true)
                {
                    // We need at least one predicate so...
                    predicate = predicate.And(x => x.Active == true || x.Active == false);
                }
                else
                {
                    predicate = predicate.And(x => x.Active == true);
                }

                if (q.Status.HasText())
                {
                    predicate = predicate.And(x => x.Status == q.Status);
                }

                queryable = dbContext.Deposits.Where(predicate);
            }

            var orderBy = request.Query?.OrderBy ?? DepositQuery.Created;
            var ascending = request.Query?.Ascending ?? false;
            switch (orderBy.ToLowerInvariant())
            {
                case "archivalgrouppath":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.ArchivalGroupPathUnderRoot)
                        : queryable.OrderByDescending(x => x.ArchivalGroupPathUnderRoot);
                    break;
                case "status":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.Status)
                        : queryable.OrderByDescending(x => x.Status);
                    break;
                case "exportedby":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.ExportedBy)
                        : queryable.OrderByDescending(x => x.ExportedBy);
                    break;
                case "exported":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.Exported)
                        : queryable.OrderByDescending(x => x.Exported);
                    break;
                case "preservedby":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.PreservedBy)
                        : queryable.OrderByDescending(x => x.PreservedBy);
                    break;
                case "preserved":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.Preserved)
                        : queryable.OrderByDescending(x => x.Preserved);
                    break;
                case "lastmodifiedby":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.LastModifiedBy)
                        : queryable.OrderByDescending(x => x.LastModifiedBy);
                    break;
                case "lastmodified":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.LastModified)
                        : queryable.OrderByDescending(x => x.LastModified);
                    break;
                case "createdby":
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.CreatedBy)
                        : queryable.OrderByDescending(x => x.CreatedBy);
                    break;
                case "created":
                default:
                    queryable = (ascending)
                        ? queryable.OrderBy(x => x.Created)
                        : queryable.OrderByDescending(x => x.Created);
                    break;
            }
            var result = await queryable.Take(500).ToListAsync(cancellationToken: cancellationToken);
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