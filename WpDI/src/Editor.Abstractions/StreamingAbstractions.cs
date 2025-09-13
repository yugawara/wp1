namespace Editor.Abstractions;

public sealed record PostSummary(long Id, string Title, string Status, string Link, string ModifiedGmt);

public sealed record StreamOptions(int WarmFirstCount = 10, int MaxBatchSize = 100);

public sealed record StreamProgress(int PagesCompleted, int TotalPagesHint);

public sealed record CachePage(
    int Page,
    IReadOnlyList<PostSummary> Items,
    string? ETag,
    int? TotalPagesHint,
    DateTimeOffset FetchedAt
);

public interface IPostCache
{
    Task<CachePage?> GetPageAsync(string scopeKey, int page, CancellationToken ct = default);
    Task UpsertPageAsync(string scopeKey, CachePage page, CancellationToken ct = default);
    IAsyncEnumerable<IReadOnlyList<PostSummary>> ReadAllPagesAsync(string scopeKey, CancellationToken ct = default);

    Task<HashSet<long>> GetAllKnownIdsAsync(string scopeKey, CancellationToken ct = default);
    Task UpsertIndexAsync(string scopeKey, IEnumerable<PostSummary> items, CancellationToken ct = default);
    Task RemoveFromIndexAsync(string scopeKey, IEnumerable<long> ids, CancellationToken ct = default);
}

public interface IContentStream
{
    IAsyncEnumerable<IReadOnlyList<PostSummary>> StreamAllCachedThenFreshAsync(
        string restBase,
        StreamOptions? options = null,
        IProgress<StreamProgress>? progress = null,
        CancellationToken ct = default);
}

public interface IPostFeed
{
    IAsyncEnumerable<IReadOnlyList<PostSummary>> Subscribe(string restBase, CancellationToken ct = default);
    Task RefreshAsync(string restBase, CancellationToken ct = default);
    IReadOnlyList<PostSummary> Current(string restBase);
}
