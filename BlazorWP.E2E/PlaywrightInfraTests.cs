using Xunit;
using BlazorWP.E2E.Helpers;

namespace BlazorWP.E2E;

[Collection("e2e")]
public class PlaywrightInfraTests
{
    private readonly BrowserFixture _fx;
    private readonly WordPressFixture _wp;
    private readonly string _app = Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "https://localhost:5173";

    public PlaywrightInfraTests(BrowserFixture fx, WordPressFixture wp) { _fx = fx; _wp = wp; }

    [Fact]
    public async Task App_Boots_And_Shows_Index()
    {
        await _fx.Page.GotoAsync(_app);
        // Assert your app shell booted (adjust selector to something stable on your index/home)
        await _fx.Page.WaitForSelectorAsync("text=BlazorWP"); // TODO: replace with your brand/header text or data-testid
    }

    [Fact]
    public async Task LocalStorage_wpEndpoint_Persists()
    {
        await _fx.Page.GotoAsync(_app);
        // Set via localStorage (BlazorWP reads this key on startup)
        await BrowserStorage.SetLocalAsync(_fx.Page, "wpEndpoint", _wp.BaseUrl.ToString());
        await _fx.Page.ReloadAsync();

        var saved = await BrowserStorage.GetLocalAsync(_fx.Page, "wpEndpoint");
        Assert.Equal(_wp.BaseUrl.ToString(), saved);
    }

    [Fact]
    public async Task IndexedDB_BlazorWPDB_Has_Notes_Store()
    {
        await _fx.Page.GotoAsync(_app);
        var stores = await BrowserStorage.ListIdbStoresAsync(_fx.Page, "BlazorWPDB");
        // At minimum, the legacy "notes" store should exist now; adjust as you add stores (e.g., "categories")
        Assert.Contains("notes", stores);
    }

    [Fact]
    public async Task WordPress_Settings_Api_Is_Reachable_With_AppPassword()
    {
        var ok = await _wp.CanReachAsync("/wp-json/wp/v2/settings");
        Assert.True(ok);
    }
}
