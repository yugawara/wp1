using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Editor.Abstractions;

namespace Editor.WordPress;

public sealed class PostFeed : IPostFeed
{
    private readonly IContentStream _stream;
    private readonly ConcurrentDictionary<string, ImmutableDictionary<long, PostSummary>> _snapshots = new();
    private readonly ConcurrentDictionary<string, Channel<IReadOnlyList<PostSummary>>> _channels = new();

    public PostFeed(IContentStream stream) => _stream = stream;

    public IReadOnlyList<PostSummary> Current(string restBase)
        => _snapshots.TryGetValue(restBase, out var snap) ? snap.Values.ToList() : new List<PostSummary>();

    public IAsyncEnumerable<IReadOnlyList<PostSummary>> Subscribe(string restBase, CancellationToken ct = default)
    {
        var channel = _channels.GetOrAdd(restBase, _ => Channel.CreateUnbounded<IReadOnlyList<PostSummary>>());
        return channel.Reader.ReadAllAsync(ct);
    }

    public async Task RefreshAsync(string restBase, CancellationToken ct = default)
    {
        var chan = _channels.GetOrAdd(restBase, _ => Channel.CreateUnbounded<IReadOnlyList<PostSummary>>());
        await foreach (var batch in _stream.StreamAllCachedThenFreshAsync(restBase, ct: ct))
        {
            // merge into snapshot
            var snap = _snapshots.GetOrAdd(restBase, ImmutableDictionary<long,PostSummary>.Empty);
            foreach (var p in batch) snap = snap.SetItem(p.Id, p);
            _snapshots[restBase] = snap;
            // broadcast full snapshot
            var full = snap.Values.ToList();
            await chan.Writer.WriteAsync(full, ct);
        }
    }
}
