using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TestMAUIApp.Services;

public class HttpService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _client;

    public HttpService()
        : this(SharedClient)
    {
    }

    public HttpService(HttpClient client)
    {
        _client = client;
    }

    public void SetBaseAddress(string baseAddress)
    {
        _client.BaseAddress = new Uri(baseAddress);
    }

    public void SetDefaultRequestHeader(string name, string value)
    {
        _client.DefaultRequestHeaders.Remove(name);
        _client.DefaultRequestHeaders.Add(name, value);
    }

    public void SetBearerToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearBearerToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;
    }

    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
        => _client.GetAsync(requestUri, cancellationToken);

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
        => _client.PostAsync(requestUri, content, cancellationToken);

    public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
        => _client.PutAsync(requestUri, content, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken = default)
        => _client.DeleteAsync(requestUri, cancellationToken);

    public Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken = default)
        => SendJsonAsync<T>(HttpMethod.Get, requestUri, content: null, ensureSuccess: true, cancellationToken);

    public Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        string requestUri,
        TRequest body,
        CancellationToken cancellationToken = default)
        => SendJsonAsync<TResponse>(HttpMethod.Post, requestUri, body, ensureSuccess: true, cancellationToken);

    public Task<TResponse?> PutJsonAsync<TRequest, TResponse>(
        string requestUri,
        TRequest body,
        CancellationToken cancellationToken = default)
        => SendJsonAsync<TResponse>(HttpMethod.Put, requestUri, body, ensureSuccess: true, cancellationToken);

    public Task<TResponse?> TryPostJsonAsync<TRequest, TResponse>(
        string requestUri,
        TRequest body,
        CancellationToken cancellationToken = default)
        => SendJsonAsync<TResponse>(HttpMethod.Post, requestUri, body, ensureSuccess: false, cancellationToken);

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        string requestUri,
        object? content,
        bool ensureSuccess,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);

        if (content is not null)
        {
            var json = JsonSerializer.Serialize(content, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (ensureSuccess)
        {
            response.EnsureSuccessStatusCode();
        }
        else if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
    }
}
