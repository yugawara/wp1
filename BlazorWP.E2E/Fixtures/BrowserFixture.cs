using Microsoft.Playwright;
using Xunit;

namespace BlazorWP.E2E;

public sealed class BrowserFixture : IAsyncLifetime
{
    public IPlaywright PW { get; private set; } = default!;
    public IBrowser Browser { get; private set; } = default!;
    public IBrowserContext Ctx { get; private set; } = default!;
    public IPage Page { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        PW = await Playwright.CreateAsync();
        Browser = await PW.Chromium.LaunchAsync(new() { Headless = true });
        Ctx = await Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        Page = await Ctx.NewPageAsync();
    }
    public async Task DisposeAsync()
    {
        await Ctx.CloseAsync();
        await Browser.CloseAsync();
        PW.Dispose();
    }
}
