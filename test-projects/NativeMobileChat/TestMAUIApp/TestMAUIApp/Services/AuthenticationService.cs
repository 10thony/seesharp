using System.Net;
using TestMAUIApp.Models;
using TestMAUIApp.Models.Api;

namespace TestMAUIApp.Services;

public class AuthenticationService
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly HttpService _http;
    private readonly LocalStorageService _localStorage;
    private readonly DataBridgeService _dataBridge;

    public AuthenticationService(
        HttpService http,
        LocalStorageService localStorage,
        DataBridgeService dataBridge)
    {
        _http = http;
        _localStorage = localStorage;
        _dataBridge = dataBridge;
    }

    public bool IsAuthenticated =>
        !string.IsNullOrEmpty(_localStorage.GetString(LocalStorageService.AuthTokenKey));

    public string? AccessToken =>
        _localStorage.GetString(LocalStorageService.AuthTokenKey);

    public async Task<AuthResponse?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest { UserName = userName, Password = password };
        var response = await _http.TryPostJsonAsync<LoginRequest, AuthResponse>(
            "api/auth/login",
            request,
            cancellationToken).ConfigureAwait(false);

        if (response is null || string.IsNullOrEmpty(response.AccessToken))
        {
            return null;
        }

        await _localStorage.CacheCredentialsAsync(userName, password).ConfigureAwait(false);
        await PersistSessionAsync(response).ConfigureAwait(false);
        return response;
    }

    public Task<(string? UserName, string? Password)> GetCachedCredentialsAsync()
        => _localStorage.GetCachedCredentialsAsync();

    public async Task LogoutAsync()
    {
        var refreshToken = await _localStorage.GetSecureAsync(LocalStorageService.RefreshTokenKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _ = await _http.TryPostJsonAsync<RefreshTokenRequest, object>(
                "api/auth/revoke",
                new RefreshTokenRequest { RefreshToken = refreshToken }).ConfigureAwait(false);
        }

        _http.ClearBearerToken();
        await _localStorage.ClearAuthAsync().ConfigureAwait(false);
    }

    public void ApplyStoredTokenToHttpClient()
    {
        var token = AccessToken;
        if (!string.IsNullOrEmpty(token))
        {
            _http.SetBearerToken(token);
        }
    }

    public async Task<T?> ExecuteAuthorizedAsync<T>(
        Func<CancellationToken, Task<T?>> request,
        CancellationToken cancellationToken = default)
    {
        ApplyStoredTokenToHttpClient();
        try
        {
            return await request(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!refreshed)
            {
                return default;
            }

            ApplyStoredTokenToHttpClient();
            return await request(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        var refreshed = await TryRefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return refreshed ? AccessToken : null;
    }

    public async Task<bool> TryRefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var refreshToken = await _localStorage.GetSecureAsync(LocalStorageService.RefreshTokenKey).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return false;
            }

            var request = new RefreshTokenRequest { RefreshToken = refreshToken };
            var response = await _http.TryPostJsonAsync<RefreshTokenRequest, AuthResponse>(
                "api/auth/refresh",
                request,
                cancellationToken).ConfigureAwait(false);

            if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            {
                _http.ClearBearerToken();
                await _localStorage.ClearSessionAsync().ConfigureAwait(false);
                return false;
            }

            await PersistSessionAsync(response).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task PersistSessionAsync(AuthResponse response)
    {
        _localStorage.SetString(LocalStorageService.AuthTokenKey, response.AccessToken);
        _http.SetBearerToken(response.AccessToken);
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            await _localStorage.SetSecureAsync(LocalStorageService.RefreshTokenKey, response.RefreshToken)
                .ConfigureAwait(false);
        }

        if (response.User is not null)
        {
            var externalId = response.User.Id.ToString();
            _localStorage.SetString(LocalStorageService.CurrentUserIdKey, externalId);

            var existing = await _dataBridge.GetUserByExternalIdAsync(externalId).ConfigureAwait(false);
            var user = existing ?? new LocalUser { ExternalId = externalId };
            user.DisplayName = response.User.DisplayName;
            user.Email = response.User.Email;
            await _dataBridge.SaveUserAsync(user).ConfigureAwait(false);
        }
    }
}
