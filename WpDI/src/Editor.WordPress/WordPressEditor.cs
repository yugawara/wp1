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

    public async Task<EditResult> UpdateAsync(long id, string html, CancellationToken ct = default)
    {
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
            var args   = new ReasonArgs("post", id);

            var meta = new
            {
                kind = "duplicate",
                reason = new
                {
                    code = reason.ToString(),   // e.g., "NotFound", "Trashed"
                    args                        // { kind: string, id: long }
                },
                originalId = id,
                timestampUtc = DateTime.UtcNow.ToString("o") // ISO-8601
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

        // Otherwise: attempt normal update in place
        var updPayload = new { content = html };
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

