// WpDI/tests/Editor.Tests/WpdiTrickyCasesTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WordPressPCL;
using WordPressPCL.Models;
using Xunit;
using Editor.WordPress; // WordPressApiService, WordPressOptions, WordPressEditor

[Collection("WP EndToEnd")]
public class WpdiTrickyCasesTests
{
    // ------------------ Service / client wiring (use your DI-friendly API service) ------------------
    private static WordPressApiService NewApi()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WP_BASE_URL");
        var user    = Environment.GetEnvironmentVariable("WP_USERNAME");
        var pass    = Environment.GetEnvironmentVariable("WP_APP_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(baseUrl), "WP_BASE_URL is not set.");
        Assert.False(string.IsNullOrWhiteSpace(user),    "WP_USERNAME is not set.");
        Assert.False(string.IsNullOrWhiteSpace(pass),    "WP_APP_PASSWORD is not set.");

        var opts = Options.Create(new WordPressOptions
        {
            BaseUrl     = baseUrl!,   // eg https://example.com
            UserName    = user!,
            AppPassword = pass!,
            Timeout     = TimeSpan.FromSeconds(20)
        });
        return new WordPressApiService(opts);
    }

    // ------------------ Helpers ------------------
    private static async Task<(string rawContent, string? modifiedGmt, JsonElement root)> GetPostEditJsonAsync(HttpClient http, long id)
    {
        // We use context=edit to get raw content + meta (WordPressPCL does not expose meta/wpdi_info by default).
        var resp = await http.GetAsync($"/wp-json/wp/v2/posts/{id}?context=edit");
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return ("", null, default);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = json.RootElement;

        string? raw = null;
        if (root.TryGetProperty("content", out var contentEl))
        {
            if (contentEl.TryGetProperty("raw", out var rawEl))
                raw = rawEl.GetString();
            else if (contentEl.TryGetProperty("rendered", out var renderedEl))
                raw = renderedEl.GetString();
        }
        string? modifiedGmt = root.TryGetProperty("modified_gmt", out var mg) ? mg.GetString() : null;
        return (raw ?? "", modifiedGmt, root);
    }

    private static string GetString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var el) ? el.GetString() ?? "" : "";

    /// <summary>
    /// Normalize meta.wpdi_info which may be absent, object, or array.
    /// Returns (present, singleInfo, isArray, length).
    /// </summary>
    private static (bool present, JsonElement singleInfo, bool isArray, int length) NormalizeWpdiInfo(JsonElement root)
    {
        if (!root.TryGetProperty("meta", out var meta)) return (false, default, false, 0);
        if (!meta.TryGetProperty("wpdi_info", out var info)) return (false, default, false, 0);

        if (info.ValueKind == JsonValueKind.Object)
            return (true, info, false, 1);

        if (info.ValueKind == JsonValueKind.Array)
        {
            var len = info.GetArrayLength();
            if (len == 0) return (false, default, true, 0);
            return (true, info[0], true, len);
        }

        return (false, default, false, 0);
    }

    private static async Task<JsonElement[]> GetRevisionsAsync(HttpClient http, long id)
    {
        var resp = await http.GetAsync($"/wp-json/wp/v2/posts/{id}/revisions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        return arr ?? Array.Empty<JsonElement>();
    }

    private static string ReadContentFromJson(JsonElement el)
    {
        if (el.TryGetProperty("content", out var c))
        {
            if (c.TryGetProperty("raw", out var rawEl)) return rawEl.GetString() ?? "";
            if (c.TryGetProperty("rendered", out var renEl)) return renEl.GetString() ?? "";
        }
        return "";
    }

    // ------------------ Tests ------------------

    // 1) Normal update: in-place save, no wpdi_info
    [Fact]
    public async Task Update_Unmodified_InPlace_NoMeta_UsingApiServiceAndPCL()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var wpcl = (await api.GetClientAsync())!;
        var editor = new WordPressEditor(http);

        // Create via WordPressEditor (code under test)
        var title = $"Wpdi Normal {System.Guid.NewGuid():N}";
        var create = await editor.CreateAsync(title, "<p>v0</p>");
        long id = create.Id;

        try
        {
            // Read with context=edit via HttpClient to get lastSeen + meta
            var (_, lastSeen, _) = await GetPostEditJsonAsync(http, id);
            Assert.False(string.IsNullOrWhiteSpace(lastSeen));

            // Update with editor
            var upd = await editor.UpdateAsync(id, "<p>v1</p>", lastSeen!);
            Assert.Equal(id, upd.Id);

            // Verify content via PCL (simple), and meta via HttpClient (for wpdi_info)
            var post = await wpcl.Posts.GetByIDAsync(id);
            var displayed = post?.Content?.Rendered ?? "";
            Assert.Contains("v1", displayed, StringComparison.OrdinalIgnoreCase);

            var (raw, _, root) = await GetPostEditJsonAsync(http, id);
            Assert.Contains("v1", raw, StringComparison.OrdinalIgnoreCase);

            var (present, _, isArr, len) = NormalizeWpdiInfo(root);
            Assert.True(!present || (isArr && len == 0)); // nothing or empty
        }
        finally
        {
            await wpcl.Posts.DeleteAsync((int)id, true);
        }
    }

    // 2) Conflict path: external edit via PCL, then editor updates with stale lastSeen. Revisions must contain both versions.
    [Fact]
    public async Task Update_WithConcurrentEdit_ConflictMetaIfDetected_AndRevisionsKeepBoth_UsingApiServiceAndPCL()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var wpcl = (await api.GetClientAsync())!;
        var editor = new WordPressEditor(http);

        // Create via editor
        var create = await editor.CreateAsync($"Wpdi Conflict {System.Guid.NewGuid():N}", "<p>initial</p>");
        long id = create.Id;

        try
        {
            // lastSeen before external change
            var (_, lastSeen, _) = await GetPostEditJsonAsync(http, id);
            Assert.False(string.IsNullOrWhiteSpace(lastSeen));

            // Ensure second boundary so modified_gmt differs on some hosts
            await Task.Delay(1500);

            // External edit via WordPressPCL
            var p = await wpcl.Posts.GetByIDAsync(id);
            p!.Content.Raw = "<p>external</p>";
            var updated = await wpcl.Posts.UpdateAsync(p);
            Assert.NotNull(updated);

            await Task.Delay(1000);

            // Now our stale update via editor
            var result = await editor.UpdateAsync(id, "<p>final</p>", lastSeen!);
            Assert.Equal(id, result.Id);

            // Verify final content + (optional) conflict warning meta
            var (raw, _, root) = await GetPostEditJsonAsync(http, id);
            Assert.Contains("final", raw, StringComparison.OrdinalIgnoreCase);

            var (present, info, _, _) = NormalizeWpdiInfo(root);
            if (present)
            {
                Assert.Equal("warning", GetString(info, "kind"));
                Assert.Equal("Conflict", GetString(info.GetProperty("reason"), "code"));
            }

            // Revisions should include both "external" and "final"
            var revs = await GetRevisionsAsync(http, id);
            var contents = revs.Take(10).Select(ReadContentFromJson).ToArray();
            Assert.Contains(contents, s => s.Contains("external", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(contents, s => s.Contains("final", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await wpcl.Posts.DeleteAsync((int)id, true);
        }
    }

    // 3) Deleted during edit: delete via PCL, then editor.UpdateAsync must create a duplicate with NotFound reason
    [Fact]
    public async Task Update_DeletedPost_CreatesDuplicateWithNotFoundReason_UsingApiServiceAndPCL()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var wpcl = (await api.GetClientAsync())!;
        var editor = new WordPressEditor(http);

        // Create via editor, then hard delete via PCL
        var created = await editor.CreateAsync($"Wpdi NotFound {System.Guid.NewGuid():N}", "<p>to be deleted</p>");
        long origId = created.Id;

        var deleted = await wpcl.Posts.DeleteAsync((int)origId, true);
        Assert.True(deleted, "Hard delete should succeed.");

        // Update via editor with stale lastSeen (any non-empty string is fine)
        var result = await editor.UpdateAsync(origId, "<p>recovered</p>", "2020-01-01T00:00:00Z");
        long newId = result.Id;
        Assert.NotEqual(origId, newId);
        Assert.Equal("draft", result.Status);

        // Verify new post details (title + wpdi_info)
        var (raw, _, root) = await GetPostEditJsonAsync(http, newId);
        Assert.Contains("recovered", raw, StringComparison.OrdinalIgnoreCase);

        var titleRendered = GetString(root.GetProperty("title"), "rendered");
        Assert.Contains($"Recovered #{origId}", titleRendered);

        var (present, info, _, _) = NormalizeWpdiInfo(root);
        Assert.True(present);
        Assert.Equal("duplicate", GetString(info, "kind"));
        Assert.Equal("NotFound", GetString(info.GetProperty("reason"), "code"));
        Assert.Equal(origId, info.GetProperty("originalId").GetInt64());

        // Old post should be gone
        var oldGet = await http.GetAsync($"/wp-json/wp/v2/posts/{origId}?context=edit");
        Assert.Equal(HttpStatusCode.NotFound, oldGet.StatusCode);

        // Cleanup
        await wpcl.Posts.DeleteAsync((int)newId, true);
    }

    // 4) Trashed during edit: trash via PCL; behavior depends on server:
    //    - If GET acts like 404/410, editor duplicates with Trashed reason.
    //    - Else, in-place update (likely Conflict) with no duplicate.
    [Fact]
    public async Task Update_TrashedPost_DuplicateOrConflictDependingOnServer_UsingApiServiceAndPCL()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var wpcl = (await api.GetClientAsync())!;
        var editor = new WordPressEditor(http);

        // Create via editor, then soft delete via PCL
        var created = await editor.CreateAsync($"Wpdi Trashed {System.Guid.NewGuid():N}", "<p>to be trashed</p>");
        long origId = created.Id;

        var trashed = await wpcl.Posts.DeleteAsync((int)origId, false); // move to trash
        Assert.True(trashed, "Soft delete (trash) should succeed.");

        // Probe behavior: some servers return 200 + status:"trash" for GET; others 404/410
        var probe = await http.GetAsync($"/wp-json/wp/v2/posts/{origId}?context=edit");
        bool behavesGone = probe.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;

        var result = await editor.UpdateAsync(origId, "<p>recovered after trash</p>", "2020-01-01T00:00:00Z");

        if (behavesGone)
        {
            // Duplicate path
            Assert.NotEqual(origId, result.Id);
            Assert.Equal("draft", result.Status);

            var (raw, _, root) = await GetPostEditJsonAsync(http, result.Id);
            Assert.Contains("recovered after trash", raw, StringComparison.OrdinalIgnoreCase);

            var titleRendered = GetString(root.GetProperty("title"), "rendered");
            Assert.Contains($"Recovered #{origId}", titleRendered);

            var (present, info, _, _) = NormalizeWpdiInfo(root);
            Assert.True(present);
            Assert.Equal("duplicate", GetString(info, "kind"));
            Assert.Equal("Trashed", GetString(info.GetProperty("reason"), "code"));
            Assert.Equal(origId, info.GetProperty("originalId").GetInt64());

            await wpcl.Posts.DeleteAsync((int)result.Id, true);
        }
        else
        {
            // In-place path (likely conflict warning if timestamps differ)
            Assert.Equal(origId, result.Id);

            var (raw, _, root) = await GetPostEditJsonAsync(http, origId);
            Assert.Contains("recovered after trash", raw, StringComparison.OrdinalIgnoreCase);

            var (present, info, _, _) = NormalizeWpdiInfo(root);
            if (present)
            {
                Assert.Equal("warning", GetString(info, "kind"));
                Assert.Equal("Conflict", GetString(info.GetProperty("reason"), "code"));
            }
        }

        // Cleanup original (hard delete)
        await wpcl.Posts.DeleteAsync((int)origId, true);
    }
}
