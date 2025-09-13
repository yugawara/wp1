using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Editor.Abstractions;

namespace Editor.WordPress;

public sealed class PostFeed : IPostFeed
{
    private readonly IContentStream _stream;
    private readonly ConcurrentDictionary<string, ImmutableDictionary<long, PostSummary>> _snapshots = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<IReadOnlyList<PostSummary>>>> _subs = new();

    public PostFeed(IContentStream stream) => _stream = stream;

    public IReadOnlyList<PostSummary> Current(string restBase)
        => _snapshots.TryGetValue(restBase, out var snap) ? snap.Values.ToList() : new List<PostSummary>();

    public IAsyncEnumerable<IReadOnlyList<PostSummary>> Subscribe(string restBase, CancellationToken ct = default)
    {
        var group = _subs.GetOrAdd(restBase, _ => new ConcurrentDictionary<Guid, Channel<IReadOnlyList<PostSummary>>>());
        var id = Guid.NewGuid();
        var ch = Channel.CreateUnbounded<IReadOnlyList<PostSummary>>();
        group[id] = ch;

        // Remove subscriber when cancelled
        ct.Register(() =>
        {
            ch.Writer.TryComplete();
            group.TryRemove(id, out _);
        });

        // Immediately send current snapshot if available
        if (_snapshots.TryGetValue(restBase, out var snap) && snap.Count > 0)
        {
            ch.Writer.TryWrite(snap.Values.ToList());
        }

        return ch.Reader.ReadAllAsync(ct);
    }

    public async Task RefreshAsync(string restBase, CancellationToken ct = default)
    {
        await foreach (var batch in _stream.StreamAllCachedThenFreshAsync(restBase, ct: ct))
        {
            // merge into snapshot
            var snap = _snapshots.GetOrAdd(restBase, ImmutableDictionary<long, PostSummary>.Empty);
            foreach (var p in batch) snap = snap.SetItem(p.Id, p);
            _snapshots[restBase] = snap;

            // broadcast full snapshot to all subscribers
            var full = snap.Values.ToList();
            if (_subs.TryGetValue(restBase, out var group))
            {
                foreach (var kv in group.ToArray())
                {
                    var writer = kv.Value.Writer;
                    if (!writer.TryWrite(full))
                    {
                        writer.TryComplete();
                        group.TryRemove(kv.Key, out _);
                    }
                }
            }
        }
    }
}
