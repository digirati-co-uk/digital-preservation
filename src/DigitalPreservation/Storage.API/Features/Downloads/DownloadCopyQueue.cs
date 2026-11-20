using System.Threading.Channels;
using DigitalPreservation.Common.Model.Download;

namespace Storage.API.Features.Downloads;

/// <summary>A copy waiting to be made: what was asked for, and where the file is in the repository.</summary>
public record DownloadCopyJob(DownloadCopy Copy, string SourceBucket, string SourceKey);

/// <summary>
/// Copies waiting to be made, held in memory. A copy lost when the Storage API restarts is never
/// made; the Preservation API notices that it has not appeared and reports the request as failed.
/// </summary>
public class DownloadCopyQueue
{
    private readonly Channel<DownloadCopyJob> queue = Channel.CreateUnbounded<DownloadCopyJob>();

    public ValueTask Add(DownloadCopyJob job, CancellationToken cancellationToken) =>
        queue.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<DownloadCopyJob> Next(CancellationToken cancellationToken) =>
        queue.Reader.ReadAsync(cancellationToken);
}

/// <summary>Makes the queued copies, one at a time.</summary>
public class DownloadCopyService(
    DownloadCopyQueue queue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DownloadCopyService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await queue.Next(stoppingToken);
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DownloadCopies>().Copy(job, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Could not copy {Digest} for download {Id}", job.Copy.Digest, job.Copy.Id);
            }
        }
    }
}
