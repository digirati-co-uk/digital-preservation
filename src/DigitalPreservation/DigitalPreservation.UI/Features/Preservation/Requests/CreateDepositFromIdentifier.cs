using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class CreateDepositFromIdentifier(string identifier, TemplateType templateType) : IRequest<Result<Deposit?>?>
{
    public string Identifier { get; set; } = identifier;
    public TemplateType TemplateType { get; } = templateType;
}

public class CreateDepositFromIdentifierHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<CreateDepositFromIdentifier, Result<Deposit?>?>
{
    public async Task<Result<Deposit?>?> Handle(CreateDepositFromIdentifier request, CancellationToken cancellationToken)
    {
        string? schema = null;
        if (request.Identifier.Length <= 7 && request.Identifier.All(char.IsDigit))
        {
            schema = SchemaAndValue.SchemaCatIrn;
        }
        else if (request.Identifier.Length >= 8 && !request.Identifier.Contains('/'))
        {
            schema = SchemaAndValue.SchemaId;
        }

        if (schema == null)
        {
            return Result.Fail<Deposit>(ErrorCodes.BadRequest,
                $"Could not determine schema for identifier {request.Identifier}");
        }
        
        var result = await preservationApiClient.CreateDepositFromIdentifier(
            schema, request.Identifier, request.TemplateType, cancellationToken);
        return result;
        
    }
}
