using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Strings;
using MediatR;
using Preservation.API.Data;
using Preservation.API.IIIF;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDepositAsIIIFManifest(string id, string baseUrl, string mediaServerBaseUrl) : IRequest<Result<Manifest>>
{
    public string Id { get; } = id;
    public string BaseUrl { get; } = baseUrl;
    
    public string MediaServerBaseUrl { get; } = mediaServerBaseUrl;
}

public class GetDepositAsIIIFManifestHandler(
    IMetsParser metsParser,
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory,
    ManifestBuilder manifestBuilder) : 
    GetDepositBase(logger, dbContext, storageApiClient, resourceMutator, workspaceManagerFactory), IRequestHandler<GetDepositAsIIIFManifest, Result<Manifest>>
{
    public async Task<Result<Manifest>> Handle(GetDepositAsIIIFManifest request, CancellationToken cancellationToken)
    {
        var getDepositResult = await GetDeposit(request.Id, cancellationToken);
        if (!getDepositResult.Success)
        {
            return Result.FailNotNull<Manifest>(getDepositResult.ErrorCode!, getDepositResult.ErrorMessage);
        }
        var wrapperResult = await metsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!);
        if (!wrapperResult.Success)
        {
            return Result.FailNotNull<Manifest>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);
        }
        var deposit = getDepositResult.Value!;
        var wrapper = wrapperResult.Value!;
        var manifest = MakeDepositManifest(deposit, wrapper, request.BaseUrl);
        manifestBuilder.MakeCanvasesAndRanges(manifest, wrapper, request.MediaServerBaseUrl);
        return Result.OkNotNull(manifest);
    }

    private Manifest MakeDepositManifest(Deposit deposit, MetsFileWrapper wrapper, string manifestBaseUrl)
    {
        const string none = "none";
        const string en = "en";
        var manifest = new Manifest
        {
            Id = manifestBaseUrl,
            Label = new LanguageMap(none, deposit.GetDisplayTitle() ?? "(no name)"),
            Metadata = [
                new LabelValuePair(en, "Total files", $"{wrapper?.Files.Count ?? 0}"),
                new LabelValuePair(none, "Archival Group Name", deposit.ArchivalGroupName ?? " - "),
                new LabelValuePair(none, "Archival Group Uri", deposit.ArchivalGroup?.ToString() ?? " - "),
                new LabelValuePair(none, "Archival Group exists", deposit.ArchivalGroupExists.ToString()),
                new LabelValuePair(en, "Status", deposit.Status),
                new LabelValuePair(en, "Submission text", deposit.SubmissionText ?? " - "),
                new LabelValuePair(en, "Created", GetDateDisplay(deposit.Created)),
                new LabelValuePair(none, "Created by", deposit.CreatedBy?.GetSlug() ?? " - "),
                new LabelValuePair(en, "Last modified", GetDateDisplay(deposit.LastModified)),
                new LabelValuePair(none, "Last modified by", deposit.LastModifiedBy?.GetSlug() ?? " - "),
                new LabelValuePair(en, "Preserved", GetDateDisplay(deposit.Preserved)),
                new LabelValuePair(none, "Preserved by", deposit.PreservedBy?.GetSlug() ?? " - "),
                new LabelValuePair(none, "Preserved version", deposit.VersionPreserved ?? " - "),
                new LabelValuePair(en, "Exported", GetDateDisplay(deposit.Exported)),
                new LabelValuePair(none, "Exported by", deposit.ExportedBy?.GetSlug() ?? " - "),
                new LabelValuePair(none, "Exported version", deposit.VersionExported ?? " - "),
            ]
        };
        return manifest;
    }



    private string? GetDateDisplay(DateTime? dt)
    {
        return !dt.HasValue ? " - " : dt?.ToLocalTime().ToString("s").Replace("T", " ");
    }
}