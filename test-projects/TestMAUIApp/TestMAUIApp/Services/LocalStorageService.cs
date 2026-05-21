namespace TestMAUIApp.Services;

public class LocalStorageService
{
    public const string AuthTokenKey = "auth_token";
    public const string RefreshTokenKey = "refresh_token";
    public const string CurrentUserIdKey = "current_user_id";
    public const string CachedUserNameKey = "cached_user_name";
    public const string CachedPasswordKey = "cached_password";

    public string? GetString(string key, string? defaultValue = null)
        => Preferences.Default.Get(key, defaultValue);

    public void SetString(string key, string value)
        => Preferences.Default.Set(key, value);

    public bool GetBool(string key, bool defaultValue = false)
        => Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value)
        => Preferences.Default.Set(key, value);

    public int GetInt(string key, int defaultValue = 0)
        => Preferences.Default.Get(key, defaultValue);

    public void SetInt(string key, int value)
        => Preferences.Default.Set(key, value);

    public bool Contains(string key)
        => Preferences.Default.ContainsKey(key);

    public void Remove(string key)
        => Preferences.Default.Remove(key);

    public async Task<string?> GetSecureAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetSecureAsync(string key, string value)
        => await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);

    public bool RemoveSecure(string key)
        => SecureStorage.Default.Remove(key);

    public async Task CacheCredentialsAsync(string userName, string password)
    {
        await SetSecureAsync(CachedUserNameKey, userName).ConfigureAwait(false);
        await SetSecureAsync(CachedPasswordKey, password).ConfigureAwait(false);
    }

    public async Task<(string? UserName, string? Password)> GetCachedCredentialsAsync()
    {
        var userName = await GetSecureAsync(CachedUserNameKey).ConfigureAwait(false);
        var password = await GetSecureAsync(CachedPasswordKey).ConfigureAwait(false);
        return (userName, password);
    }

    public Task ClearAuthAsync()
    {
        ClearSession();
        RemoveSecure(CachedUserNameKey);
        RemoveSecure(CachedPasswordKey);
        return Task.CompletedTask;
    }

    public Task ClearSessionAsync()
    {
        ClearSession();
        return Task.CompletedTask;
    }

    private void ClearSession()
    {
        Remove(AuthTokenKey);
        Remove(RefreshTokenKey);
        Remove(CurrentUserIdKey);
        RemoveSecure(AuthTokenKey);
        RemoveSecure(RefreshTokenKey);
    }
}
