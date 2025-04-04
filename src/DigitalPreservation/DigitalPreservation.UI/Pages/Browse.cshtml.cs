using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Preservation.Client;

namespace DigitalPreservation.UI.Pages;

public class BrowseModel(
    IMediator mediator, 
    IPreservationApiClient preservationApiClient, 
    ILogger<BrowseModel> logger,
    IMemoryCache memoryCache) : PageModel
{
    
    private readonly IMemoryCache memoryCache = memoryCache;
    public PreservedResource? Resource { get; set; }
    public string? PathUnderRoot { get; set; }
    public string? ArchivalGroupPath { get; set; }
    
    // When browsing an Archival Group, use the OCFL object for speed
    public ArchivalGroup? CachedArchivalGroup { get; set; }
    
    
    // When we are on an archival group
    public List<Deposit> Deposits { get; set; } = [];
    public Dictionary<string, List<ImportJobResult>> ImportJobResultsForNewDeposits { get; set; } = new();

    
    public async Task<IActionResult> OnGet(string? pathUnderRoot, [FromQuery] string? view, [FromQuery] string? version)
    {
        PathUnderRoot = pathUnderRoot;
        var resourcePath = $"{PreservedResource.BasePathElement}/{pathUnderRoot ?? string.Empty}";
        Resource = await TryGetResourceFromArchivalGroup(pathUnderRoot, version);
        if (Resource == null)
        {
            var result = await mediator.Send(new GetResource(resourcePath));
            if (result.Success)
            {
                Resource = result.Value;
            }
        }

        if (Resource == null)
        {
            return NotFound();
        }
        
        var name = Resource!.Name ?? Resource!.Id!.GetSlug();
        switch (Resource!.Type)
        {
            case nameof(ArchivalGroup):
                
                if (view == "mets")
                {
                    var metsResult = await preservationApiClient.GetMetsStream(resourcePath);
                    if (metsResult is { Item1: not null, Item2: not null })
                    {
                        return File(metsResult.Item1, metsResult.Item2);
                    }
                }
                ViewData["Title"] = $"📦 {name}";
                ArchivalGroupPath = PathUnderRoot;
                var query = new DepositQuery
                {
                    ArchivalGroupPath = PathUnderRoot,
                    OrderBy = DepositQuery.Created,
                    ShowAll = true
                };
                var depositsResult = await mediator.Send(new GetDeposits(query));
                if (depositsResult.Success)
                {
                    Deposits = depositsResult.Value!.Deposits ?? [];
                    await GetJobsForActiveDeposits();
                }
                break;
            case nameof(Container):
                ViewData["Title"] = $"📁 {name}";
                break;
            case nameof(Binary):
                ViewData["Title"] = $"🗎 {name}";
                break;
            case "RepositoryRoot":
                ViewData["Title"] = $"Browse Repository";
                break;
        }

        if (Resource.PartOf != null)
        {
            ArchivalGroupPath = Resource.PartOf.GetPathUnderRoot();
        }
     
        return Page();
    }

    private async Task<PreservedResource?> TryGetResourceFromArchivalGroup(string? pathUnderRoot, string? version)
    {
        var resourcePath = $"{PreservedResource.BasePathElement}/{pathUnderRoot}";
        if (pathUnderRoot is null or "/" or "")
        {
            return null;
        }
        var parts = pathUnderRoot.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            // cannot be an AG or in an AG, we don't allow them in the root
            return null;
        }

        // Is there an archival group in the cache that matches this resourcePath, or is the parent of this resourcePath?
        var testPath = pathUnderRoot;
        while (testPath.HasText() && testPath != "/" && testPath.Contains('/'))
        {
            var headCacheKey = $"AG-{testPath}?version=";
            var cacheKey = $"{headCacheKey}{version ?? ""}";
            logger.LogInformation("Seeing if there is an Archival Group in the cache for {cacheKey}", cacheKey);
            CachedArchivalGroup = memoryCache.TryGetValue(cacheKey, out ArchivalGroup? archivalGroup) ? archivalGroup : null;
            if (CachedArchivalGroup != null)
            {
                logger.LogInformation("Archival Group was found for {cacheKey}", cacheKey);
                break;
            }
            testPath = testPath.GetParent();
        }

        if (CachedArchivalGroup == null)
        {
            // If not, get the resource.
            var result = await mediator.Send(new GetResource(resourcePath));
            if (result.Success)
            {
                ArchivalGroup? archivalGroupToBeCached = null;
                var preservedResource = result.Value;
                if (preservedResource == null)
                {
                    // This is going to cause a problem in the rest of the flow
                    logger.LogWarning("No resource returned for {resourcePath}", resourcePath);
                    return null;
                }
                if (preservedResource is ArchivalGroup group)
                {
                    // If it is an archival group, hang on to it.
                    logger.LogInformation("{resourcePath} is an Archival Group", resourcePath);
                    archivalGroupToBeCached = group;
                }
                else if (preservedResource.PartOf != null)
                {
                    // If it's partOf an archival group, get the archival group.
                    result = await mediator.Send(new GetResource(preservedResource.PartOf.AbsolutePath));
                    archivalGroupToBeCached = result.Value as ArchivalGroup;
                    if (archivalGroupToBeCached != null)
                    {
                        logger.LogInformation("ArchivalGroup {archivalGroup} recovered from .PartOf for {resourcePath}",
                            archivalGroupToBeCached.Id!.AbsolutePath, resourcePath);
                    }
                    else
                    {
                        logger.LogInformation("No archival group found on path {resourcePath}", resourcePath);
                    }
                }

                if (archivalGroupToBeCached != null)
                {
                    var agPathUnderRoot = archivalGroupToBeCached.GetPathUnderRoot();
                    var headCacheKey = $"AG-{agPathUnderRoot}?version=";
                    var cacheKey = $"{headCacheKey}{version ?? ""}";
                    // If we now have an archival group, cache it with a key that uses the version (including version=(blank))
                    memoryCache.Set(cacheKey, archivalGroupToBeCached, TimeSpan.FromMinutes(3));
                    // Inspect the archival group. If it's the latest version, and we haven't already cached it at version=(blank),
                    if (cacheKey != headCacheKey)
                    {
                        // a specific version was requested, but if it's the latest, cache it under the headCacheKey
                        var latest = archivalGroupToBeCached.Versions!.OrderBy(v => v.MementoTimestamp).Last();
                        if (archivalGroupToBeCached.Version!.OcflVersion == latest.OcflVersion)
                        {
                            logger.LogInformation("Setting headCacheKey {headCacheKey} for original cacheKey {cacheKey}",
                                headCacheKey, cacheKey);
                            memoryCache.Set(headCacheKey, archivalGroupToBeCached, TimeSpan.FromMinutes(3));
                        }
                    }
                    CachedArchivalGroup = archivalGroupToBeCached;
                }
                // Might as well use the resource we just got rather than do any more with the cached version
                return preservedResource;
            }
        }

        if (CachedArchivalGroup == null)
        {
            logger.LogInformation("No CachedArchivalGroup retrievable.");
            return null;
        }

        if (CachedArchivalGroup.Id!.AbsolutePath.RemoveStart("/") == resourcePath.RemoveStart("/"))
        {
            logger.LogInformation("{resourcePath} is the archival group itself, returning", resourcePath);
            return CachedArchivalGroup;
        }
        
        // /repository/folder/folder/ag/folder/folder/file
        var localPath = resourcePath
            .RemoveStart("/")
            .RemoveStart(CachedArchivalGroup.Id!.AbsolutePath.RemoveStart("/")!)
            .RemoveStart("/");
        var resourceInAg = CachedArchivalGroup.FindResource(localPath);
        if (resourceInAg is not null)
        {
            logger.LogInformation("Found resource of type {type} for {localPath} within ArchivalGroup {archivalGroup}",
                resourceInAg.Type, localPath, CachedArchivalGroup.Id!.AbsolutePath);
        }
        else
        {
            logger.LogInformation("No resource found at path {localPath} within Archival Group {archivalGroup}",
                localPath, CachedArchivalGroup.Id!.AbsolutePath);
        }
        return resourceInAg;
    }

    private async Task GetJobsForActiveDeposits()
    {
        foreach (var deposit in Deposits)
        {
            if (deposit.Status == DepositStates.New)
            {
                var result = await DepositJobResultFetcher.GetImportJobResults(deposit.Id!.GetSlug()!, mediator);
                if (result is { Success: true, Value: not null })
                {
                    ImportJobResultsForNewDeposits[deposit.Id!.GetSlug()!] = result.Value;
                }
            }
        }
    }

    public async Task<IActionResult> OnPost(string? pathUnderRoot, string? containerSlug, string? containerTitle)
    {
        if (containerSlug.IsNullOrWhiteSpace())
        {
            TempData["ContainerError"] = "Missing container name";
            return Redirect(Request.Path);
        }
        
        var slug = containerSlug.ToLowerInvariant();
        if (ValidateNewContainer(pathUnderRoot, slug, containerTitle))
        {
            var result = await mediator.Send(new CreateContainer(pathUnderRoot, slug, containerTitle));
            if(result is { Success: true, Value: not null })
            {
                TempData["ContainerSuccess"] = "Created Container " + result.Value;
            }
            else
            {
                TempData["ContainerError"] = "Failed to create Container: " + result.CodeAndMessage();
            }
            return Redirect(Request.Path);
        }

        TempData["ContainerError"] = "Invalid container file path - only a-z, 0-9 and .-_ are allowed.";
        return Redirect(Request.Path);
    }

    private bool ValidateNewContainer(string? path, string slug, string? containerTitle)
    {
        return PreservedResource.ValidSlug(slug);
    }
}