using System;
using System.Net.Http;
using Editor.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Editor.WordPress
{
    public static class WordPressEditingDI
    {
        // WPDI does not configure HttpClient—host provides it (Basic or Nonce).
        public static IServiceCollection AddWordPressEditingFromHttp(
            this IServiceCollection services,
            Func<IServiceProvider, HttpClient> httpProvider)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (httpProvider == null) throw new ArgumentNullException(nameof(httpProvider));

            services.AddScoped<IPostEditor>(sp =>
            {
                var http = httpProvider(sp)
                           ?? throw new InvalidOperationException("HttpClient provider returned null.");
                return new WordPressEditor(http);
            });

            return services;
        }
    }
}
