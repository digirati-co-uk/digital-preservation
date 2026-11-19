using System.Threading.Channels;

namespace Pipeline.API.Features.Pipeline;

public interface IPipelineQueue
{
    ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken);
}

/// <summary>
/// Basic implementation of pipeline job running service using bounded queue for managing / processing
/// </summary>
/// <remarks>This is purely for demo purposes - this would likely use SQS </remarks>
public class InProcessPipelineQueue : IPipelineQueue
{
    private readonly Channel<PipelineJobMessage?> queue;

    public InProcessPipelineQueue()
    {
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        queue = Channel.CreateBounded<PipelineJobMessage?>(options);
    }

    /// <summary>
    /// Restored for issue #352 (local HTTP delivery): #231 removed this when the only caller,
    /// POST /pipeline's own direct-queue path, was deleted in favour of proxying to the
    /// Preservation API. It's back because the Preservation API now needs a way to feed this
    /// queue when FeatureFlags:UseLocalPipelineDelivery is on - this time called from a
    /// dedicated internal endpoint (PipelineController.QueuePipelineJob), not from the
    /// public-facing POST /pipeline. Lives on the concrete class rather than IPipelineQueue, which
    /// stays dequeue-only: SqsPipelineQueue has no equivalent writer - a local HTTP delivery
    /// publishes to SNS instead when this flag is off, exactly as today.
    ///
    /// TryWrite, not WriteAsync (issue #352 review): the queue holds 10 messages and jobs run one
    /// at a time, so an 11th local job used to block the whole POST /pipeline/jobs request - and
    /// the caller's own HttpClient - until space freed up or its own timeout gave up, rather than
    /// refusing the request straight away. TryWrite returns immediately either way regardless of
    /// the channel's FullMode, so the caller can turn a full queue into an honest "try again" (503)
    /// instead of a long, confusing stall.
    /// </summary>
    public bool TryQueueRequest(string jobIdentifier, string depositName, string? runUser)
        => queue.Writer.TryWrite(new PipelineJobMessage { JobIdentifier = jobIdentifier, DepositName = depositName, RunUser = runUser });

    public ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}

