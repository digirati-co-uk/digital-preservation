using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using DigitalPreservation.Utils;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Content;
using IIIF.Presentation.V3.Strings;
using MediatR;
using Newtonsoft.Json.Linq;
using Preservation.API.Features.Repository.Requests;
using Preservation.API.IIIF;

namespace Preservation.API.Features.Iiif.Requests;

/// <summary>
/// An Archival Group projected as a IIIF Manifest (issue #261): <see cref="ManifestKind.Vanilla"/>
/// for <c>?view=iiif</c>, <see cref="ManifestKind.IiifCs"/> for <c>?view=iiif-cs</c>.
/// </summary>
/// <param name="ArchivalGroup">The group as the Preservation API presents it (URIs already mutated).</param>
/// <param name="Kind">Which manifest.</param>
/// <param name="ManifestId">The manifest's id: the request URI.</param>
/// <param name="Root">The API's own base URI, no trailing slash.</param>
public record GetArchivalGroupAsIIIFManifest(ArchivalGroup ArchivalGroup, ManifestKind Kind, string ManifestId, string Root)
    : IRequest<Result<Manifest>>;

public class GetArchivalGroupAsIIIFManifestHandler(
    IMediator mediator,
    IMetsParser metsParser,
    IDerivedAccessConditionResolver derivedResolver,
    IAccessConditionProfileProvider profileProvider,
    ManifestProjection projection) : IRequestHandler<GetArchivalGroupAsIIIFManifest, Result<Manifest>>
{
    public async Task<Result<Manifest>> Handle(GetArchivalGroupAsIIIFManifest request, CancellationToken cancellationToken)
    {
        var archivalGroup = request.ArchivalGroup;
        var mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, true))
                   ?? archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, false));
        if (mets is null)
        {
            return Result.FailNotNull<Manifest>(ErrorCodes.NotFound, "No METS file found in archival group");
        }

        var streamResult = await mediator.Send(new GetBinaryStream(mets.Id!.AbsolutePath), cancellationToken);
        if (streamResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<Manifest>(streamResult.ErrorCode ?? ErrorCodes.UnknownError, streamResult.ErrorMessage);
        }
        XDocument xMets;
        await using (var stream = streamResult.Value)
        {
            xMets = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        }
        var wrapperResult = metsParser.GetMetsFileWrapperFromXDocument(mets.Id, xMets);
        if (wrapperResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<Manifest>(wrapperResult.ErrorCode ?? ErrorCodes.UnknownError, wrapperResult.ErrorMessage);
        }

        // Uri.UnescapeDataString, not GetPathUnderRoot's decode, which is form decoding and turns '+' into a space.
        var pathUnderRoot = Uri.UnescapeDataString(archivalGroup.Id!.GetPathUnderRoot()!);
        var binaryUris = BinaryUris(archivalGroup);
        var storageMap = archivalGroup.StorageMap;
        // The storage map is keyed by Fedora's spelling of each path, escaped (a space is "%20", a
        // '#' is "-_-percent-23-_-"); METS paths are not. Unescape once, as WorkspaceManager does.
        var originFiles = storageMap == null ? null : Unescaped(storageMap.Files);
        var context = new ProjectionContext
        {
            Kind = request.Kind,
            IdBase = $"{request.Root}/iiif/{pathUnderRoot.EscapePathElements()}/",
            PlaceholderBase = $"{request.Root}/placeholders/",
            PathPrefix = pathUnderRoot,
            Rendering = request.Kind == ManifestKind.Vanilla
                ? file => binaryUris.GetValueOrDefault(file.LocalPath)
                : null,
            Origin = request.Kind == ManifestKind.IiifCs && storageMap != null
                ? file => originFiles!.TryGetValue(file.LocalPath, out var origin) ? S3Uri(storageMap, origin.FullPath) : null
                : null,
            // The version the path last changed in. Not the version in its content path, which can be
            // older: OCFL stores identical bytes once, so reverted content points back at the old copy.
            ContentVersion = request.Kind == ManifestKind.IiifCs && storageMap != null
                ? file => originFiles!.TryGetValue(file.LocalPath, out var origin)
                    ? origin.ContentVersion ?? origin.FullPath.Split('/')[0]
                    : null
                : null,
            ObjectVersion = request.Kind == ManifestKind.IiifCs ? storageMap?.Version.OcflVersion : null,
            DerivedTypes = await derivedResolver.GetManagedTypesAsync(cancellationToken),
            Statements = (await profileProvider.GetProfileAsync(cancellationToken))?.Statements
        };

        var manifest = new Manifest
        {
            Id = request.ManifestId,
            Label = new LanguageMap("none", archivalGroup.Name ?? archivalGroup.GetSlug() ?? pathUnderRoot),
            SeeAlso =
            [
                new ExternalResource("Dataset")
                {
                    Id = $"{archivalGroup.Id!.AbsoluteUri}?view={ViewValues.Mets}",
                    Format = "application/xml",
                    Profile = "http://www.loc.gov/METS/",
                    Label = new LanguageMap("en", "METS")
                },
                new ExternalResource("Dataset")
                {
                    Id = archivalGroup.Id.AbsoluteUri,
                    Format = "application/json",
                    Label = new LanguageMap("en", "Archival Group")
                }
            ]
        };
        projection.Project(manifest, wrapperResult.Value, context);

        if (request.Kind == ManifestKind.IiifCs && storageMap?.FormerFiles != null)
        {
            manifest.AdditionalProperties[ManifestProjection.FormerAssetsTerm] = FormerAssets(storageMap, context);
        }
        return Result.OkNotNull(manifest);
    }

    /// <summary>
    /// The assets a builder may have registered from earlier versions that this one no longer has:
    /// every file below objects/ in an earlier version and not in this one. It doesn't say whether a
    /// builder acted on any version, so a builder decides what to delete. A file that is still here
    /// but has become a linked file (so an adjunct, not an asset) is not listed.
    /// </summary>
    private static JArray FormerAssets(DigitalPreservation.Common.Model.Storage.StorageMap storageMap, ProjectionContext context)
    {
        var former = new JArray();
        foreach (var (path, file) in Unescaped(storageMap.FormerFiles!).OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith($"{FolderNames.Objects}/"))
            {
                continue;
            }
            former.Add(new JObject
            {
                ["id"] = ManifestProjection.IdFor(path, context),
                ["lastVersion"] = file.LastVersion,
                ["origin"] = S3Uri(storageMap, file.FullPath)
            });
        }
        return former;
    }

    private static Dictionary<string, T> Unescaped<T>(Dictionary<string, T> byFedoraPath)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (path, value) in byFedoraPath)
        {
            result[path.UnEscapePathElementsNoHashes()] = value;
        }
        return result;
    }

    // The content path exactly as stored, escapes and all: it is the S3 key, and IIIF-CS takes the
    // key from an s3:// origin literally.
    private static string S3Uri(DigitalPreservation.Common.Model.Storage.StorageMap storageMap, string fullPath) =>
        $"s3://{storageMap.Root}/{storageMap.ObjectPath.Trim('/')}/{fullPath}";

    private static Dictionary<string, string> BinaryUris(ArchivalGroup archivalGroup)
    {
        // AbsoluteUri, not ToString(): ToString() unescapes, and a URI with a space in it is not a URI.
        var prefix = archivalGroup.Id!.AbsoluteUri.TrimEnd('/') + "/";
        var (_, binaries) = archivalGroup.Flatten();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in binaries.Select(b => b.Id))
        {
            var relative = id?.AbsoluteUri.RemoveStart(prefix);
            if (relative != null)
            {
                // Fedora's '#' replacement as well as percent-escapes, to match the METS path.
                result[relative.UnEscapePathElementsNoHashes()] = id!.AbsoluteUri;
            }
        }
        return result;
    }
}
