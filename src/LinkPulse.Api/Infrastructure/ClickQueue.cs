using System.Threading.Channels;
using LinkPulse.Api.Domain;

namespace LinkPulse.Api.Infrastructure;

public sealed record ClickEvent(int LinkId, DateTime ClickedAt, string? Referrer, DeviceType Device);

/// <summary>
/// Decouples redirects from analytics writes: a redirect only enqueues a click,
/// and <see cref="ClickRecorder"/> persists them in batches.
/// </summary>
public sealed class ClickQueue
{
    private readonly Channel<ClickEvent> _channel = Channel.CreateBounded<ClickEvent>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public bool TryEnqueue(ClickEvent click) => _channel.Writer.TryWrite(click);
}
