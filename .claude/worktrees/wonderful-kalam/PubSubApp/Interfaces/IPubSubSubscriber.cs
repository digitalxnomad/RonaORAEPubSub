
using System.Threading;
using System.Threading.Tasks;

public interface IPubSubSubscriber
{
    Task StartAsync(CancellationToken cancellationToken);
}
