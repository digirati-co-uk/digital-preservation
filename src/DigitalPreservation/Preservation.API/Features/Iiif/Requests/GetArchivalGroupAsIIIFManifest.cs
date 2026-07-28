using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Strings;
using MediatR;
using Preservation.API.IIIF;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Iiif.Requests;

public class GetArchivalGroupAsIIIFManifest(string path, string manifestUrl, string iiifBaseUrl)
    : IRequest<Result<Manifest>>
{
    public string Path { get; } = path;

    /// <summary>URL used as manifest.Id — the canonical location of this manifest.</summary>
    public string ManifestUrl { get; } = manifestUrl;

    /// <summary>Base URL (trailing slash) for canvas and range IDs.</summary>
    public string IiifBaseUrl { get; } = iiifBaseUrl;
}

public class GetArchivalGroupAsIIIFManifestHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    IMetsParser metsParser,
    ManifestBuilder manifestBuilder) : IRequestHandler<GetArchivalGroupAsIIIFManifest, Result<Manifest>>
{
    public async Task<Result<Manifest>> Handle(
        GetArchivalGroupAsIIIFManifest request,
        CancellationToken cancellationToken)
    {
        var repositoryPath = $"/{PreservedResource.BasePathElement}/{request.Path}";
        var resourceResult = await storageApiClient.GetResource(repositoryPath);
        if (!resourceResult.Success)
            return Result.FailNotNull<Manifest>(resourceResult.ErrorCode!, resourceResult.ErrorMessage);

        if (resourceResult.Value is not ArchivalGroup archivalGroup)
            return Result.FailNotNull<Manifest>(ErrorCodes.NotFound, "Not found");

        resourceMutator.MutateStorageResource(archivalGroup);

        var mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, true))
                ?? archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, false));
        if (mets is null)
            return Result.FailNotNull<Manifest>(ErrorCodes.NotFound, "No METS file found in archival group");

        var streamResult = await storageApiClient.GetBinaryStream(mets.Id!.AbsolutePath);
        if (!streamResult.Success || streamResult.Value is null)
            return Result.FailNotNull<Manifest>(streamResult.ErrorCode!, streamResult.ErrorMessage);

        var xMets = XDocument.Load(streamResult.Value);
        var wrapperResult = metsParser.GetMetsFileWrapperFromXDocument(mets.Id, xMets);
        if (!wrapperResult.Success || wrapperResult.Value is null)
            return Result.FailNotNull<Manifest>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);

        var originUris = BuildOriginUriLookup(archivalGroup);

        var manifest = new Manifest
        {
            Id = request.ManifestUrl,
            Label = new LanguageMap("none", archivalGroup.Name ?? archivalGroup.GetSlug() ?? request.Path)
        };
        manifestBuilder.MakeCanvasesAndRanges(
            manifest,
            wrapperResult.Value,
            request.IiifBaseUrl,
            mediaServerBaseUrl: null,
            originUriResolver: file => originUris.TryGetValue(file.LocalPath, out var uri)
                ? uri
                : $"{archivalGroup.Id}/{file.LocalPath.EscapePathElements()}");

        return Result.OkNotNull(manifest);
    }

    private static Dictionary<string, string> BuildOriginUriLookup(ArchivalGroup archivalGroup)
    {
        var agUriPrefix = archivalGroup.Id!.ToString().TrimEnd('/') + "/";
        var (_, allBinaries) = archivalGroup.Flatten();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binary in allBinaries)
        {
            if (binary.Id is null) continue;
            var relative = binary.Id.ToString().RemoveStart(agUriPrefix);
            if (relative is null) continue;
            result[Uri.UnescapeDataString(relative)] = binary.Id.ToString();
        }
        return result;
    }
}
