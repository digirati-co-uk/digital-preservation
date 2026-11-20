using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Storage.Client;

namespace Preservation.API.Features.Downloads;

/// <summary>
/// Requests to download preserved files (RFC 011). This keeps the record of each request; the
/// Storage API makes the copy, signs the links to it and removes it.
/// </summary>
/// <remarks>
/// Nothing runs in the background here. A request's status is brought up to date when someone
/// looks at it: that is when a finished copy is noticed, and when an expired one is removed. The
/// downloads bucket's own lifecycle rule removes any copy that nobody looked at again.
/// </remarks>
public class Downloads(
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    IIdentityMinter identityMinter,
    IOptions<DownloadOptions> options)
{
    public async Task<Result<Download>> Request(NewDownload newDownload, string requestedBy, CancellationToken cancellationToken)
    {
        var repositoryPath = StringUtils.BuildPath(true, PreservedResource.BasePathElement, newDownload.Path);
        var resource = await storageApiClient.GetResource(repositoryPath);
        if (resource.Value is not Binary binary)
        {
            return Result.FailNotNull<Download>(ErrorCodes.NotFound, "No file at " + newDownload.Path);
        }
        if (binary.PartOf is null || !binary.Digest.HasText())
        {
            return Result.FailNotNull<Download>(ErrorCodes.Unprocessable,
                newDownload.Path + " has no Archival Group or no digest, so it can't be copied.");
        }

        var now = DateTime.UtcNow;
        var request = new DownloadRequest
        {
            Id = identityMinter.MintIdentity(nameof(DownloadRequest)),
            Path = newDownload.Path.Trim('/'),
            Name = FileName(binary, newDownload.Path),
            Digest = binary.Digest,
            Size = binary.Size,
            ContentType = binary.ContentType,
            RequestedBy = requestedBy,
            Reason = newDownload.Reason.HasText() ? newDownload.Reason.Trim() : null,
            Created = now,
            Expires = now.AddDays(options.Value.LifetimeDays),
            Status = DownloadStatus.Copying
        };

        var started = await storageApiClient.StartDownloadCopy(new DownloadCopy
        {
            Id = request.Id,
            ArchivalGroup = binary.PartOf,
            Digest = request.Digest,
            Name = request.Name,
            ContentType = request.ContentType
        }, cancellationToken);
        if (started.Failure)
        {
            return Result.FailNotNull<Download>(started.ErrorCode!, started.ErrorMessage);
        }

        dbContext.DownloadRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.OkNotNull(ToDownload(request));
    }

    public async Task<List<Download>> List(CancellationToken cancellationToken)
    {
        var requests = await dbContext.DownloadRequests
            .Include(r => r.LinksIssued)
            .OrderByDescending(r => r.Created)
            .Take(200)
            .ToListAsync(cancellationToken);
        foreach (var request in requests)
        {
            await BringUpToDate(request, cancellationToken);
        }
        return requests.Select(ToDownload).ToList();
    }

    public async Task<Result<Download>> Get(string id, CancellationToken cancellationToken)
    {
        var request = await Find(id, cancellationToken);
        return request is null ? NotFound<Download>(id) : Result.OkNotNull(ToDownload(request));
    }

    /// <summary>A short-lived link to the copy, with a record of who it was handed to.</summary>
    public async Task<Result<DownloadLink>> IssueLink(string id, string issuedTo, CancellationToken cancellationToken)
    {
        var request = await Find(id, cancellationToken);
        if (request is null)
        {
            return NotFound<DownloadLink>(id);
        }
        if (request.Status != DownloadStatus.Ready)
        {
            return Result.FailNotNull<DownloadLink>(ErrorCodes.Conflict,
                $"Download {id} is {request.Status}; there is a link only while it is {DownloadStatus.Ready}.");
        }

        var link = await storageApiClient.GetDownloadLink(id, cancellationToken);
        if (link.Success)
        {
            request.LinksIssued.Add(new DownloadLinkIssued
            {
                DownloadRequestId = id, IssuedTo = issuedTo, Issued = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return link;
    }

    /// <summary>Removes the copy now. The record of the request stays.</summary>
    public async Task<Result> Delete(string id, CancellationToken cancellationToken)
    {
        var request = await Find(id, cancellationToken);
        if (request is null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No download " + id);
        }
        if (request.Status is DownloadStatus.Copying or DownloadStatus.Ready)
        {
            return await RemoveCopy(request, DownloadStatus.Deleted, cancellationToken);
        }
        return Result.Ok();
    }

    private async Task<DownloadRequest?> Find(string id, CancellationToken cancellationToken)
    {
        var request = await dbContext.DownloadRequests
            .Include(r => r.LinksIssued)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is not null)
        {
            await BringUpToDate(request, cancellationToken);
        }
        return request;
    }

    private async Task BringUpToDate(DownloadRequest request, CancellationToken cancellationToken)
    {
        if (request.Status is not (DownloadStatus.Copying or DownloadStatus.Ready))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (request.Expires <= now)
        {
            await RemoveCopy(request, DownloadStatus.Expired, cancellationToken);
            return;
        }
        if (request.Status == DownloadStatus.Ready)
        {
            return;
        }

        var copy = await storageApiClient.GetDownloadCopyStatus(request.Id, cancellationToken);
        if (copy is { Success: true, Value.Ready: true })
        {
            request.Status = DownloadStatus.Ready;
        }
        else if (request.Created.AddMinutes(options.Value.CopyTimeoutMinutes) <= now)
        {
            request.Status = DownloadStatus.Failed;
            request.Error = "The copy was not made. The Storage API's log says why. Request the file again.";
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result> RemoveCopy(DownloadRequest request, string newStatus, CancellationToken cancellationToken)
    {
        var deleted = await storageApiClient.DeleteDownloadCopy(request.Id, cancellationToken);
        if (deleted.Success)
        {
            request.Status = newStatus;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return deleted;
    }

    // The name in the repository can be any text, so it may not be usable as a file name.
    private static string FileName(Binary binary, string path)
    {
        var name = binary.Name.HasText() ? binary.Name : path.TrimEnd('/').Split('/')[^1];
        return name.Replace('/', '_').Replace('\\', '_');
    }

    private static Result<T> NotFound<T>(string id) =>
        Result.FailNotNull<T>(ErrorCodes.NotFound, "No download " + id);

    private static Download ToDownload(DownloadRequest request) => new()
    {
        Id = request.Id,
        Path = request.Path,
        Name = request.Name,
        Digest = request.Digest,
        Size = request.Size,
        ContentType = request.ContentType,
        RequestedBy = request.RequestedBy,
        Reason = request.Reason,
        Created = request.Created,
        Expires = request.Expires,
        Status = request.Status,
        Error = request.Error,
        LinksIssued = request.LinksIssued.Count
    };
}
