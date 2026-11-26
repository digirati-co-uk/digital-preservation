using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Content;
using IIIF.Presentation.V3.Strings;
using MediatR;
using Preservation.API.Data;
using Preservation.API.IIIF;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

/// <summary>
/// A Deposit projected as a vanilla IIIF Manifest (issue #261). Every canvas is a placeholder, and
/// no canvas links to its file: a Deposit's files are reachable only through the UI and the API.
/// </summary>
/// <param name="id">The Deposit.</param>
/// <param name="tokenBaseUrl">The manifest's id: the tokenised URL it is served from.</param>
/// <param name="plainBaseUrl">Base (trailing slash) of canvas and range ids.</param>
/// <param name="root">The API's own base URI, no trailing slash.</param>
public class GetDepositAsIIIFManifest(string id, string tokenBaseUrl, string plainBaseUrl, string root) : IRequest<Result<Manifest>>
{
    public string Id { get; } = id;
    public string TokenBaseUrl { get; } = tokenBaseUrl;
    public string PlainBaseUrl { get; } = plainBaseUrl;
    public string Root { get; } = root;
}

public class GetDepositAsIIIFManifestHandler(
    IMetsParser metsParser,
    ILogger<GetDepositAsIIIFManifestHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory,
    IDerivedAccessConditionResolver derivedResolver,
    IAccessConditionProfileProvider profileProvider,
    ManifestProjection projection) : 
    GetDepositBase(logger, dbContext, storageApiClient, resourceMutator, workspaceManagerFactory, metsParser), IRequestHandler<GetDepositAsIIIFManifest, Result<Manifest>>
{
    public async Task<Result<Manifest>> Handle(GetDepositAsIIIFManifest request, CancellationToken cancellationToken)
    {
        var getDepositResult = await GetDeposit(request.Id, cancellationToken);
        if (!getDepositResult.Success)
        {
            return Result.FailNotNull<Manifest>(getDepositResult.ErrorCode!, getDepositResult.ErrorMessage);
        }
        var wrapperResult = await MetsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!);
        if (!wrapperResult.Success)
        {
            return Result.FailNotNull<Manifest>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);
        }
        var deposit = getDepositResult.Value;
        var wrapper = wrapperResult.Value!;
        var manifest = MakeDepositManifest(deposit, wrapper, request.TokenBaseUrl);
        manifest.SeeAlso =
        [
            new ExternalResource("Dataset")
            {
                Id = $"{request.Root}/deposits/{request.Id}/mets",
                Format = "application/xml",
                Profile = "http://www.loc.gov/METS/",
                Label = new LanguageMap("en", "METS")
            },
            new ExternalResource("Dataset")
            {
                Id = $"{request.Root}/deposits/{request.Id}",
                Format = "application/json",
                Label = new LanguageMap("en", "Deposit")
            }
        ];
        projection.Project(manifest, wrapper, new ProjectionContext
        {
            Kind = ManifestKind.Vanilla,
            IdBase = request.PlainBaseUrl,
            PlaceholderBase = $"{request.Root}/placeholders/",
            // Ids as the files will have them once preserved, when the Deposit names its Archival Group.
            PathPrefix = deposit.ArchivalGroup?.GetPathUnderRoot() is { } agPath ? Uri.UnescapeDataString(agPath) : null,
            DerivedTypes = await derivedResolver.GetManagedTypesAsync(cancellationToken),
            Statements = (await profileProvider.GetProfileAsync(cancellationToken))?.Statements
        });
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



    private static string? GetDateDisplay(DateTime? dt)
    {
        return !dt.HasValue ? " - " : dt?.ToLocalTime().ToString("s").Replace("T", " ");
    }
}