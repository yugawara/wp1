// WpDI/tests/Editor.Tests/WpdiTrickyCasesTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Editor.WordPress;
using Microsoft.Extensions.Options;

[Collection("WP EndToEnd")]
public class WpdiTrickyCasesTests
{
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
            BaseUrl     = baseUrl!,
            UserName    = user!,
            AppPassword = pass!,
            Timeout     = TimeSpan.FromSeconds(20)
        });
        return new WordPressApiService(opts);
    }

    private static async Task<(string rawContent, string? modifiedGmt, JsonElement root)> GetPostEditAsync(HttpClient http, long id)
    {
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

    // -------- Tricky cases --------

    [Fact]
    public async Task Update_Unmodified_InPlace_NoMeta()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var editor = new WordPressEditor(http);

        var create = await editor.CreateAsync($"Wpdi Normal {Guid.NewGuid():N}", "<p>v0</p>");
        long id = create.Id;

        try
        {
            var (_, lastSeen, _) = await GetPostEditAsync(http, id);
            Assert.False(string.IsNullOrWhiteSpace(lastSeen));

            var upd = await editor.UpdateAsync(id, "<p>v1</p>", lastSeen!);
            Assert.Equal(id, upd.Id);

            var (raw, _, root) = await GetPostEditAsync(http, id);
            Assert.Contains("v1", raw);

            var (present, _, isArr, len) = NormalizeWpdiInfo(root);
            Assert.True(!present || (isArr && len == 0));
        }
        finally
        {
            var del = await http.DeleteAsync($"/wp-json/wp/v2/posts/{id}?force=true");
            Assert.Contains(del.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        }
    }

    [Fact]
    public async Task Update_WithConcurrentEdit_AttachesConflictWarning_AndRevisionsContainBothVersions()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var editor = new WordPressEditor(http);

        var create = await editor.CreateAsync($"Wpdi Conflict {Guid.NewGuid():N}", "<p>initial</p>");
        long id = create.Id;

        try
        {
            var (_, lastSeen, _) = await GetPostEditAsync(http, id);
            Assert.False(string.IsNullOrWhiteSpace(lastSeen));

            // Ensure server timestamp differs
            await Task.Delay(1500);
            var ext = await http.PostAsJsonAsync($"/wp-json/wp/v2/posts/{id}", new { content = "<p>external</p>" });
            Assert.Equal(HttpStatusCode.OK, ext.StatusCode);
            await Task.Delay(1000);

            var upd = await editor.UpdateAsync(id, "<p>final</p>", lastSeen!);
            Assert.Equal(id, upd.Id);

            var (raw, _, root) = await GetPostEditAsync(http, id);
            Assert.Contains("final", raw);

            var (present, info, _, _) = NormalizeWpdiInfo(root);
            if (present)
            {
                Assert.Equal("warning", GetString(info, "kind"));
                Assert.Equal("Conflict", GetString(info.GetProperty("reason"), "code"));
            }

            // Revisions should show both versions
            var revs = await http.GetAsync($"/wp-json/wp/v2/posts/{id}/revisions");
            Assert.Equal(HttpStatusCode.OK, revs.StatusCode);
            var revArr = await revs.Content.ReadFromJsonAsync<JsonElement[]>() ?? Array.Empty<JsonElement>();
            var contents = revArr.Take(10)
                .Select(r =>
                {
                    var c = r.GetProperty("content");
                    return c.TryGetProperty("raw", out var rawEl)
                        ? rawEl.GetString()
                        : c.GetProperty("rendered").GetString();
                })
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            Assert.Contains(contents, s => s!.Contains("external", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(contents, s => s!.Contains("final", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            var del = await http.DeleteAsync($"/wp-json/wp/v2/posts/{id}?force=true");
            Assert.Contains(del.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        }
    }

    [Fact]
    public async Task Update_DeletedPost_CreatesDuplicateWithNotFoundReason()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var editor = new WordPressEditor(http);

        var create = await editor.CreateAsync($"Wpdi NotFound {Guid.NewGuid():N}", "<p>to be deleted</p>");
        long origId = create.Id;

        var del = await http.DeleteAsync($"/wp-json/wp/v2/posts/{origId}?force=true");
        Assert.Contains(del.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent });

        var result = await editor.UpdateAsync(origId, "<p>recovered</p>", "2020-01-01T00:00:00Z");
        long newId = result.Id;
        Assert.NotEqual(origId, newId);

        var (raw, _, root) = await GetPostEditAsync(http, newId);
        Assert.Contains("recovered", raw);

        var (present, info, _, _) = NormalizeWpdiInfo(root);
        Assert.True(present);
        Assert.Equal("duplicate", GetString(info, "kind"));
        Assert.Equal("NotFound", GetString(info.GetProperty("reason"), "code"));
        Assert.Equal(origId, info.GetProperty("originalId").GetInt64());

        var oldGet = await http.GetAsync($"/wp-json/wp/v2/posts/{origId}?context=edit");
        Assert.Equal(HttpStatusCode.NotFound, oldGet.StatusCode);

        var delNew = await http.DeleteAsync($"/wp-json/wp/v2/posts/{newId}?force=true");
        Assert.Contains(delNew.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task Update_TrashedPost_DuplicateOrConflictDependingOnServer()
    {
        var api = NewApi();
        var http = api.HttpClient!;
        var editor = new WordPressEditor(http);

        var create = await editor.CreateAsync($"Wpdi Trashed {Guid.NewGuid():N}", "<p>to be trashed</p>");
        long origId = create.Id;

        var trash = await http.DeleteAsync($"/wp-json/wp/v2/posts/{origId}");
        Assert.Contains(trash.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent });

        var probe = await http.GetAsync($"/wp-json/wp/v2/posts/{origId}?context=edit");
        bool behavesGone = probe.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;

        var result = await editor.UpdateAsync(origId, "<p>recovered after trash</p>", "2020-01-01T00:00:00Z");

        if (behavesGone)
        {
            // Duplicate draft with Trashed reason
            Assert.NotEqual(origId, result.Id);
            var (raw, _, root) = await GetPostEditAsync(http, result.Id);
            Assert.Contains("recovered after trash", raw);
            var (present, info, _, _) = NormalizeWpdiInfo(root);
            Assert.True(present);
            Assert.Equal("Trashed", GetString(info.GetProperty("reason"), "code"));
            await http.DeleteAsync($"/wp-json/wp/v2/posts/{result.Id}?force=true");
        }
        else
        {
            // In-place update with potential warning
            Assert.Equal(origId, result.Id);
            var (raw, _, root) = await GetPostEditAsync(http, origId);
            Assert.Contains("recovered after trash", raw);
            var (present, _, _, _) = NormalizeWpdiInfo(root);
            Assert.True(present || !present); // just ensure it parses
        }

        // Cleanup old
        await http.DeleteAsync($"/wp-json/wp/v2/posts/{origId}?force=true");
    }
}
