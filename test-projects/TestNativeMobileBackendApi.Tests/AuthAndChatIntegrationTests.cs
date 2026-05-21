using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TestNativeMobileBackendApi.Tests;

public class AuthAndChatIntegrationTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;

    public AuthAndChatIntegrationTests(TestApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SyntheticClients_CanAuthenticate_AndExchangeChatMessages()
    {
        var userOne = await LoginAsync("synthetic01", "Password1!");
        var userTwo = await LoginAsync("synthetic02", "Password1!");

        var messageOne = $"synthetic01 says hello {Guid.NewGuid():N}";
        var messageTwo = $"synthetic02 replies {Guid.NewGuid():N}";

        await PostChatMessageAsync(userOne.AccessToken, messageOne);
        await PostChatMessageAsync(userTwo.AccessToken, messageTwo);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/chat/messages?limit=200");
        listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userTwo.AccessToken);
        using var listResponse = await _client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();

        var payload = await listResponse.Content.ReadAsStringAsync();
        var messages = JsonSerializer.Deserialize<List<ChatMessageDto>>(payload, JsonOptions) ?? [];

        Assert.Contains(messages, m => m.UserName == "synthetic01" && m.Message == messageOne);
        Assert.Contains(messages, m => m.UserName == "synthetic02" && m.Message == messageTwo);
    }

    [Fact]
    public async Task RefreshToken_RotationReuseAttack_RevokesUserSessionFamily()
    {
        var initial = await LoginAsync("synthetic03", "Password1!");

        var firstRefresh = await RefreshAsync(initial.RefreshToken, expectedStatus: HttpStatusCode.OK);
        Assert.NotNull(firstRefresh);
        Assert.NotEqual(initial.RefreshToken, firstRefresh!.RefreshToken);

        // Reusing an already-rotated token is treated as a token-family compromise.
        await RefreshAsync(initial.RefreshToken, expectedStatus: HttpStatusCode.Unauthorized);
        await RefreshAsync(firstRefresh.RefreshToken, expectedStatus: HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminRoleEndpoint_BlocksSelfRoleChange_AndCanPromoteAnotherUser()
    {
        var admin = await LoginAsync("admin", "Password1!");

        var selfChange = await PutAuthorizedJsonAsync(
            $"/api/admin/users/{admin.User!.Id}/role",
            new UpdateUserRoleRequest("User"),
            admin.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, selfChange.StatusCode);

        var target = await LoginAsync("synthetic04", "Password1!");
        var promote = await PutAuthorizedJsonAsync(
            $"/api/admin/users/{target.User!.Id}/role",
            new UpdateUserRoleRequest("Admin"),
            admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        var targetRelogin = await LoginAsync("synthetic04", "Password1!");
        Assert.Equal("Admin", targetRelogin.User!.Role);

        // Roll back role to keep fixture data stable across reruns.
        var demote = await PutAuthorizedJsonAsync(
            $"/api/admin/users/{target.User.Id}/role",
            new UpdateUserRoleRequest("User"),
            admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
    }

    private async Task<AuthResponseDto> LoginAsync(string userName, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AuthResponseDto>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize auth response.");
    }

    private async Task<AuthResponseDto?> RefreshAsync(string refreshToken, HttpStatusCode expectedStatus)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken));
        Assert.Equal(expectedStatus, response.StatusCode);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AuthResponseDto>(payload, JsonOptions);
    }

    private async Task PostChatMessageAsync(string accessToken, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/messages")
        {
            Content = JsonContent.Create(new PostChatMessageRequest(message)),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PutAuthorizedJsonAsync<TBody>(string uri, TBody body, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private sealed record LoginRequest(string UserName, string Password);
    private sealed record RefreshTokenRequest(string RefreshToken);
    private sealed record PostChatMessageRequest(string Message);
    private sealed record UpdateUserRoleRequest(string Role);

    private sealed class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public UserProfileDto? User { get; set; }
    }

    private sealed class UserProfileDto
    {
        public Guid Id { get; set; }
        public string Role { get; set; } = string.Empty;
    }

    private sealed class ChatMessageDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

public class TestApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
