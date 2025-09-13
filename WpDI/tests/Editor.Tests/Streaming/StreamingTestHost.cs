// WpDI/tests/Editor.Tests/Streaming/StreamingTestHost.cs
using Editor.Abstractions;
using Editor.WordPress;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class StreamingTestHost
{
    public static (ServiceProvider sp, WordPressApiService api) Build()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WP_BASE_URL")!;
        var user    = Environment.GetEnvironmentVariable("WP_USERNAME")!;
        var pass    = Environment.GetEnvironmentVariable("WP_APP_PASSWORD")!;

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WordPressOptions>>(
            Options.Create(new WordPressOptions { BaseUrl = baseUrl, UserName = user, AppPassword = pass, Timeout = TimeSpan.FromSeconds(20) })
        );
        services.AddSingleton<WordPressApiService>();

        services.AddSingleton<IPostCache, MemoryPostCache>();
        services.AddWpdiStreaming(sp => sp.GetRequiredService<WordPressApiService>().HttpClient!);

        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<WordPressApiService>());
    }
}
