using System.Threading.Channels;

namespace Pipeline.API.Features.Pipeline;

public interface IPipelineQueue
{
    ValueTask QueueRequest(string jobIdentifier, string depositName, string? runUser, CancellationToken cancellationToken);
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
    
    public ValueTask QueueRequest(string jobIdentifier, string depositName, string? runUser, CancellationToken cancellationToken)
        => queue.Writer.WriteAsync(new PipelineJobMessage { JobIdentifier = jobIdentifier, DepositName = depositName, RunUser = runUser}, cancellationToken);

    public ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}

