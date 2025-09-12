// WpDI/src/Editor.Abstractions/IPostEditor.cs
namespace Editor.Abstractions;

public interface IPostEditor
{
    Task<EditResult> CreateAsync(string title, string html, CancellationToken ct = default);

    /// <summary>
    /// Update a post with Last-Write-Wins semantics.
    /// The caller must pass the lastSeenModifiedUtc (as reported by WordPress "modified_gmt")
    /// from when the editor loaded the resource. WpDI will:
    /// - If the resource is missing (404) → duplicate draft with reason=NotFound.
    /// - If trashed (410) → duplicate draft with reason=Trashed.
    /// - If still present but modified since lastSeenModifiedUtc → overwrite in place,
    ///   but also attach a Conflict warning in wpdi_info meta so the UI can warn the user.
    /// - If unmodified → overwrite in place normally.
    /// - On other HTTP errors → throw WordPressApiException.
    /// </summary>
    Task<EditResult> UpdateAsync(
        long id,
        string html,
        string lastSeenModifiedUtc,
        CancellationToken ct = default);
}

public sealed record EditResult(long Id, string Url, string Status);
