using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace BlazorWP.E2E;

public sealed class WordPressFixture : IAsyncLifetime
{
    public Uri BaseUrl { get; }
    public string Username { get; }
    public string AppPassword { get; }

    public WordPressFixture()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WP_BASE_URL");
        var user    = Environment.GetEnvironmentVariable("WP_USERNAME");
        var pass    = Environment.GetEnvironmentVariable("WP_APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            throw new InvalidOperationException("Set WP_BASE_URL, WP_USERNAME, WP_APP_PASSWORD.");
        BaseUrl = new Uri(baseUrl!);
        Username = user!;
        AppPassword = pass!;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<bool> CanReachAsync(string relative)
    {
        using var http = new HttpClient { BaseAddress = BaseUrl };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{AppPassword}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        using var res = await http.GetAsync(relative);
        return res.IsSuccessStatusCode;
    }
}
