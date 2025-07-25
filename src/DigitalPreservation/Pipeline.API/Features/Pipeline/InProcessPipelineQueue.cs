using System.Threading.Channels;

namespace Pipeline.API.Features.Pipeline;

public interface IPipelineQueue
{
    ValueTask QueueRequest(string depositName, CancellationToken cancellationToken);
    ValueTask<string> DequeueRequest(CancellationToken cancellationToken);
}

/// <summary>
/// Basic implementation of pipeline job running service using bounded queue for managing / processing
/// </summary>
/// <remarks>This is purely for demo purposes - this would likely use SQS </remarks>
public class InProcessPipelineQueue : IPipelineQueue
{
    private readonly Channel<string> queue;
    
    public InProcessPipelineQueue()
    {
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        queue = Channel.CreateBounded<string>(options);
    }
    
    public ValueTask QueueRequest(string depositName, CancellationToken cancellationToken)
        => queue.Writer.WriteAsync(depositName, cancellationToken);

    public ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}
