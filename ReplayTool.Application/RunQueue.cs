using System.Threading.Channels;

namespace ReplayTool.Application;

// Singleton channel that decouples run creation from background execution.
public sealed class RunQueue
{
    private readonly Channel<(Guid CaseId, Guid RunId, bool IsRetry)> _channel =
        Channel.CreateUnbounded<(Guid, Guid, bool)>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<(Guid CaseId, Guid RunId, bool IsRetry)> Writer => _channel.Writer;
    public ChannelReader<(Guid CaseId, Guid RunId, bool IsRetry)> Reader => _channel.Reader;
}
