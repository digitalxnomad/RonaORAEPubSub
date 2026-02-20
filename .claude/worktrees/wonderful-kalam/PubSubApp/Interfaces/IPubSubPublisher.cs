
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IPubSubPublisher
{
    Task PublishMessageAsync(string message, IDictionary<string, string>? attributes = null);
}
