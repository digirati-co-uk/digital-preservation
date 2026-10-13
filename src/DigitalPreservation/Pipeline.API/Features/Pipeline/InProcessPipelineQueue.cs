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
    /// </summary>
    public ValueTask QueueRequest(string jobIdentifier, string depositName, string? runUser, CancellationToken cancellationToken)
        => queue.Writer.WriteAsync(new PipelineJobMessage { JobIdentifier = jobIdentifier, DepositName = depositName, RunUser = runUser }, cancellationToken);

    public ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}

