using System.Threading.Channels;
using DigitalPreservation.Common.Model.Import;

namespace Storage.API.Features.Import;

public interface IImportJobQueue
{
    ValueTask QueueRequest(string jobIdentifier, CancellationToken cancellationToken);
    ValueTask<string> DequeueRequest(CancellationToken cancellationToken);
}

/// <summary>
/// Basic implementation of import job running service using bounded queue for managing / processing
/// </summary>
/// <remarks>This is purely for demo purposes - this would likely use SQS </remarks>
public class InProcessImportJobQueue : IImportJobQueue
{
    private readonly Channel<string> queue;
    
    public InProcessImportJobQueue()
    {
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        queue = Channel.CreateBounded<string>(options);
    }
    
    public ValueTask QueueRequest(string jobIdentifier, CancellationToken cancellationToken)
        => queue.Writer.WriteAsync(jobIdentifier, cancellationToken);

    public ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
        => queue.Reader.ReadAsync(cancellationToken);
}
