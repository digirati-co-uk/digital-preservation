using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class CreateDepositFromIdentifier(string identifier) : IRequest<Result<Deposit?>?>
{
    public string Identifier { get; set; } = identifier;
}

public class CreateDepositFromIdentifierHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<CreateDepositFromIdentifier, Result<Deposit?>?>
{
    public async Task<Result<Deposit?>?> Handle(CreateDepositFromIdentifier request, CancellationToken cancellationToken)
    {
        string? schema = null;
        if (request.Identifier.Length <= 6 && request.Identifier.All(char.IsDigit))
        {
            schema = "catirn";
        }
        else if (request.Identifier.Length >= 8 && !request.Identifier.Contains('/'))
        {
            schema = "id";
        }

        if (schema == null)
        {
            return Result.Fail<Deposit>(ErrorCodes.BadRequest,
                $"Could not determine schema for identifier {request.Identifier}");
        }
        
        var result = await preservationApiClient.CreateDepositFromIdentifier(
            schema, request.Identifier, cancellationToken);
        return result;
        
    }
}