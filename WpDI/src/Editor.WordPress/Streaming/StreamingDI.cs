using Microsoft.Extensions.DependencyInjection;
using Editor.Abstractions;

namespace Editor.WordPress;

public static class StreamingDI
{
    public static IServiceCollection AddWpdiStreaming(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient> httpProvider)
    {
        services.AddScoped<IContentStream>(sp =>
        {
            var http = httpProvider(sp) ?? throw new InvalidOperationException("HttpClient is null");
            var cache = sp.GetRequiredService<IPostCache>();
            return new ContentStream(http, cache);
        });
        services.AddSingleton<IPostFeed, PostFeed>();
        return services;
    }
}
