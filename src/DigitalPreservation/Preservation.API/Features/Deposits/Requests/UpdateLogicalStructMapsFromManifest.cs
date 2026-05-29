using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Workspace;
using IIIF.Presentation.V3;
using MediatR;
using Range = IIIF.Presentation.V3.Range;
using Preservation.API.Data;
using Preservation.API.IIIF;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class UpdateLogicalStructMapsFromManifest(
    string id,
    Manifest manifest,
    string iiifBaseUrl,
    string rawManifestJson) : IRequest<Result>
{
    public string Id { get; } = id;
    public Manifest Manifest { get; } = manifest;
    public string IiifBaseUrl { get; } = iiifBaseUrl;
    public string RawManifestJson { get; } = rawManifestJson;
}

public class UpdateLogicalStructMapsFromManifestHandler(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory)
    : GetDepositBase(logger, dbContext, storageApiClient, resourceMutator, workspaceManagerFactory),
      IRequestHandler<UpdateLogicalStructMapsFromManifest, Result>
{
    public async Task<Result> Handle(UpdateLogicalStructMapsFromManifest request, CancellationToken cancellationToken)
    {
        var depositResult = await GetDeposit(request.Id, cancellationToken);
        if (!depositResult.Success)
            return Result.Fail(depositResult.ErrorCode!, depositResult.ErrorMessage);

        var deposit = depositResult.Value!;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit);

        var canvasIdToLocalPath = ManifestParser.BuildCanvasIdToLocalPath(request.Manifest, request.IiifBaseUrl);

        // Validate: every canvas referenced in ranges must be one we know about
        var unknownCanvas = FindUnknownCanvas(request.Manifest.Structures, canvasIdToLocalPath);
        if (unknownCanvas != null)
            return Result.Fail(ErrorCodes.BadRequest, $"Canvas '{unknownCanvas}' is not part of this deposit.");

        var logicalRanges = ManifestParser.ExtractLogicalRanges(
            request.Manifest, request.IiifBaseUrl, request.RawManifestJson);

        foreach (var logicalRange in logicalRanges)
        {
            var saveResult = await workspaceManager.SetLogicalStructMap(logicalRange);
            if (!saveResult.Success)
                return saveResult;
        }

        return Result.Ok();
    }

    private static string? FindUnknownCanvas(
        List<Range>? structures,
        Dictionary<string, string> knownCanvasIds)
    {
        if (structures == null) return null;
        foreach (var range in structures)
        {
            var found = FindUnknownCanvasInRange(range, knownCanvasIds);
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindUnknownCanvasInRange(
        Range range,
        Dictionary<string, string> knownCanvasIds)
    {
        foreach (var item in range.Items ?? [])
        {
            if (item is Canvas c)
            {
                var id = c.Id ?? "";
                var hashIdx = id.IndexOf('#');
                var baseId = hashIdx >= 0 ? id[..hashIdx] : id;
                if (!knownCanvasIds.ContainsKey(baseId)) return baseId;
            }
            else if (item is SpecificResource sr)
            {
                var sourceId = (sr.Source as Canvas)?.Id;
                if (sourceId != null && !knownCanvasIds.ContainsKey(sourceId)) return sourceId;
            }
            else if (item is Range childRange)
            {
                var found = FindUnknownCanvasInRange(childRange, knownCanvasIds);
                if (found != null) return found;
            }
        }
        return null;
    }

}
