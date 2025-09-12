// WpDI/src/Editor.WordPress/WordPressEditor.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Editor.Abstractions;

namespace Editor.WordPress;

public sealed class WordPressEditor : IPostEditor
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WordPressEditor(HttpClient http) => _http = http;

    public async Task<EditResult> CreateAsync(string title, string html, CancellationToken ct = default)
    {
        var payload = new { title, status = "draft", content = html };
        using var res = await _http.PostAsync(
            "/wp-json/wp/v2/posts",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
            ct);
        return await ParseOrThrow(res, ct);
    }

    /// <summary>
    /// Update a post with Last-Write-Wins semantics.
    /// If the server's current modified timestamp differs from the caller's lastSeenModifiedUtc,
    /// we still update in-place but attach a Conflict warning in meta.wpdi_info so the UI can notify the user.
    /// If the target is missing (404) or trashed (410), we create a duplicate draft with typed reason meta.
    /// </summary>
    public async Task<EditResult> UpdateAsync(long id, string html, string lastSeenModifiedUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lastSeenModifiedUtc))
            throw new ArgumentException("lastSeenModifiedUtc is required (ISO-8601 or WP modified_gmt).", nameof(lastSeenModifiedUtc));

        // Preflight (bypass caches)
        using var preReq = new HttpRequestMessage(
            HttpMethod.Get,
            $"/wp-json/wp/v2/posts/{id}?context=edit&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
        );
        preReq.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        preReq.Headers.Pragma.ParseAdd("no-cache");

        using var pre = await _http.SendAsync(preReq, ct);

        if (pre.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Create a duplicate draft with *typed* meta (UI will localize)
            var reason = pre.StatusCode == HttpStatusCode.NotFound ? ReasonCode.NotFound : ReasonCode.Trashed;

            var meta = new
            {
                kind = "duplicate",
                reason = new
                {
                    code = reason.ToString(),      // "NotFound" | "Trashed"
                    args = new { kind = "post", id }
                },
                originalId = id,
                timestampUtc = DateTime.UtcNow.ToString("o")
            };

            var duplicateTitle = $"Recovered #{id} {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            var payload = new
            {
                title = duplicateTitle,
                status = "draft",
                content = html,
                meta = new { wpdi_info = meta }
            };

            using var resDup = await _http.PostAsync(
                "/wp-json/wp/v2/posts",
                new StringContent(System.Text.Json.JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
                ct);
            return await ParseOrThrow(resDup, ct);
        }

        // Otherwise: attempt normal update in place (and detect divergence for LWW warning)
        string? serverModifiedUtc = null;
        if (pre.IsSuccessStatusCode)
        {
            var preBody = await pre.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(preBody);
                if (doc.RootElement.TryGetProperty("modified_gmt", out var mg))
                    serverModifiedUtc = mg.GetString();
            }
            catch
            {
                // If parsing fails, we won't emit conflict meta (still proceed with LWW)
            }
        }

        var conflict =
            !string.IsNullOrWhiteSpace(serverModifiedUtc) &&
            !string.Equals(serverModifiedUtc, lastSeenModifiedUtc, StringComparison.Ordinal);

        // Build update payload: always update content; attach conflict warning meta when divergent
        object updPayload = conflict
            ? new
            {
                content = html,
                meta = new
                {
                    wpdi_info = new
                    {
                        kind = "warning",
                        reason = new
                        {
                            code = ReasonCode.Conflict.ToString(), // "Conflict"
                            args = new { kind = "post", id }
                        },
                        baseModifiedUtc = lastSeenModifiedUtc,
                        serverModifiedUtc = serverModifiedUtc,
                        timestampUtc = DateTime.UtcNow.ToString("o")
                    }
                }
            }
            : new { content = html };

        using var res = await _http.PostAsync(
            $"/wp-json/wp/v2/posts/{id}",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(updPayload, Json), Encoding.UTF8, "application/json"),
            ct);
        return await ParseOrThrow(res, ct);
    }

    private static async Task<EditResult> ParseOrThrow(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new WordPressApiException(res.StatusCode, body);
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        return new EditResult(
            r.GetProperty("id").GetInt64(),
            r.GetProperty("link").GetString()!,
            r.GetProperty("status").GetString()!
        );
    }
}
