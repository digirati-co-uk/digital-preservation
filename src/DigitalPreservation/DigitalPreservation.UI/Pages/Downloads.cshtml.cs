using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.UI.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Preservation.Client;

namespace DigitalPreservation.UI.Pages;

/// <summary>
/// Requests to download preserved files (RFC 011): ask for a file from its page in Browse, then
/// come here for the link once its copy is ready.
/// </summary>
public class Downloads(IPreservationApiClient preservationApiClient) : PageModel
{
    public List<Download> Requests { get; private set; } = [];

    /// <summary>Why the list could not be shown, for example that this user may not download.</summary>
    public string? ListError { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.GetDownloads(cancellationToken);
        if (result.Success)
        {
            Requests = result.Value!;
        }
        else
        {
            ListError = result.ErrorMessage;
        }
    }

    public async Task<IActionResult> OnPostRequest(string path, string? reason, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.RequestDownload(path, reason, cancellationToken);
        return BackToList(result, $"Requested {path}. Its link appears here when the copy is ready.");
    }

    public async Task<IActionResult> OnPostDelete(string id, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.DeleteDownload(id, cancellationToken);
        return BackToList(result, "Deleted the copy for download " + id);
    }

    /// <summary>Sends the browser to a fresh, short-lived link to the file.</summary>
    public async Task<IActionResult> OnGetFile(string id, CancellationToken cancellationToken)
    {
        var link = await preservationApiClient.IssueDownloadLink(id, cancellationToken);
        return link.Success ? Redirect(link.Value!.Url.ToString()) : BackToList(link, "");
    }

    private RedirectToPageResult BackToList(Result result, string successMessage)
    {
        if (result.Success)
        {
            TempData[TempDataKeys.Updated] = successMessage;
        }
        else
        {
            TempData[TempDataKeys.Error] = result.ErrorMessage;
        }
        return RedirectToPage();
    }
}
