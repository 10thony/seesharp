using TestMAUIApp.Models;
using TestMAUIApp.Models.Api;

namespace TestMAUIApp.Services;

public class UserService
{
    private readonly HttpService _http;
    private readonly LocalStorageService _localStorage;
    private readonly DataBridgeService _dataBridge;
    private readonly AuthenticationService _authentication;

    public UserService(
        HttpService http,
        LocalStorageService localStorage,
        DataBridgeService dataBridge,
        AuthenticationService authentication)
    {
        _http = http;
        _localStorage = localStorage;
        _dataBridge = dataBridge;
        _authentication = authentication;
    }

    public async Task<LocalUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var externalId = _localStorage.GetString(LocalStorageService.CurrentUserIdKey);
        if (!string.IsNullOrEmpty(externalId))
        {
            var cached = await _dataBridge.GetUserByExternalIdAsync(externalId).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        if (!_authentication.IsAuthenticated)
        {
            return null;
        }

        var profile = await _authentication.ExecuteAuthorizedAsync(
            ct => _http.GetJsonAsync<UserProfile>("api/auth/me", ct),
            cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        externalId = profile.Id.ToString();
        var user = await _dataBridge.GetUserByExternalIdAsync(externalId).ConfigureAwait(false)
            ?? new LocalUser { ExternalId = externalId };
        user.DisplayName = profile.DisplayName;
        user.Email = profile.Email;
        await _dataBridge.SaveUserAsync(user).ConfigureAwait(false);
        _localStorage.SetString(LocalStorageService.CurrentUserIdKey, externalId);
        return user;
    }

    public async Task<List<ChatUserOption>> GetChatUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated)
        {
            return [];
        }

        var remote = await _authentication.ExecuteAuthorizedAsync(
            ct => _http.GetJsonAsync<List<ChatUserDto>>("api/chat/users", ct),
            cancellationToken).ConfigureAwait(false);

        if (remote is null)
        {
            return [];
        }

        return remote
            .Select(dto => new ChatUserOption
            {
                UserId = dto.Id.ToString(),
                UserName = dto.UserName,
                DisplayName = dto.DisplayName,
            })
            .OrderBy(u => u.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
