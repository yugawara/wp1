using WordPressPCL;
using System.Net.Http;
using System;
using System.Threading.Tasks;
using Editor.WordPress;
using BlazorWP.Data;

namespace BlazorWP;

public sealed class WordPressApiService : IWordPressApiService
{
    private readonly AuthMessageHandler _auth;
    private readonly AppFlags _flags;
    private WordPressClient? _client;
    private HttpClient? _httpClient;

    public WordPressApiService(AuthMessageHandler auth, AppFlags flags)
    {
        _auth = auth;
        _flags = flags;
        _flags.OnChange += () =>
        {
            // If wp URL changed, drop the cached client so next call re-inits
            _client = null;
            _httpClient = null;
        };
    }


    public void SetEndpoint(string endpoint)
    {
        var baseUrl = endpoint.TrimEnd('/') + "/wp-json/";
        _httpClient = new HttpClient(_auth) { BaseAddress = new Uri(baseUrl) };
        _client = new WordPressClient(_httpClient);
    }

    public Task<WordPressClient?> GetClientAsync()
    {
        if (_client != null)
            return Task.FromResult<WordPressClient?>(_client);

        var endpoint = _flags.WpUrl;
        if (string.IsNullOrEmpty(endpoint))
            return Task.FromResult<WordPressClient?>(null);

        SetEndpoint(endpoint);
        return Task.FromResult(_client);
    }

    public WordPressClient? Client => _client;
    public HttpClient? HttpClient => _httpClient;
}
