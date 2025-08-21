using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages;

public class Ocfl(IMediator mediator) : PageModel
{
    public async Task OnGet(string pathUnderRoot, [FromQuery] string? version)
    {
        PathUnderRoot = pathUnderRoot;
        var lightweightAgResult = await mediator.Send(new GetLightweightResource(pathUnderRoot, version));
        if (lightweightAgResult is { Success: true, Value: ArchivalGroup })
        {
            ArchivalGroupLightweight = (ArchivalGroup)lightweightAgResult.Value;
            Title = ArchivalGroupLightweight.Name ?? PathUnderRoot.GetSlug();
            var storageMapResult = await mediator.Send(new GetStorageMap(pathUnderRoot, version));
            if (storageMapResult is { Success: true, Value: not null })
            {
                StorageMap = storageMapResult.Value;
                Version = StorageMap.Version;
                ViewData["Title"] = $"VERSION {Version.OcflVersion} of {Title}";
            }
            else
            {
                TempData["Error"] = "Could not retrieve Storage Map: " + storageMapResult.CodeAndMessage();
            }
        }
        else
        {
            TempData["Error"] = "Could not retrieve 'Lightweight' Archival Group: " + lightweightAgResult.CodeAndMessage();
        }
    }
    
    public ArchivalGroup? ArchivalGroupLightweight { get; set; }

    public required string PathUnderRoot { get; set; }
    public ObjectVersion? Version { get; set; }

    public string? Title { get; set; }
    
    public StorageMap? StorageMap { get; set; }

    public string GetVersionRowClass(ObjectVersion version)
    {
        if (version.Equals(Version))
        {
            return "table-info";
        }

        if (version.Equals(StorageMap!.HeadVersion))
        {
            return "table-primary";
        }
        return "";
    }
}