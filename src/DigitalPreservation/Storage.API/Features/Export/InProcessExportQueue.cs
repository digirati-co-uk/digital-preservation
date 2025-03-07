using System.Threading.Channels;

namespace Storage.API.Features.Export;

public interface IExportQueue
{
    ValueTask QueueRequest(string exportIdentifier, CancellationToken cancellationToken);
    ValueTask<string> DequeueRequest(CancellationToken cancellationToken);
}

public class InProcessExportQueue : IExportQueue
{
    private readonly Channel<string> queue;
    
    public InProcessExportQueue()
    {
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        queue = Channel.CreateBounded<string>(options);
    }
    
    public ValueTask QueueRequest(string exportIdentifier, CancellationToken cancellationToken)
        => queue.Writer.WriteAsync(exportIdentifier, cancellationToken);

    public ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}