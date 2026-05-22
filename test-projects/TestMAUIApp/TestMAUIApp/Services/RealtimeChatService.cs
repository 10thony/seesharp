using System.Net;
using Microsoft.AspNetCore.SignalR.Client;
using TestMAUIApp.Configuration;

namespace TestMAUIApp.Services;

public class RealtimeChatService
{
    private readonly AuthenticationService _authentication;
    private readonly AppOptions _appOptions;
    private HubConnection? _connection;
    private string? _accessToken;

    public RealtimeChatService(AuthenticationService authentication, AppOptions appOptions)
    {
        _authentication = authentication;
        _appOptions = appOptions;
    }

    public event EventHandler? MessageReceived;

    public async Task EnsureConnectedAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        _accessToken = accessToken;
        _connection ??= CreateConnection();

        if (_connection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
        {
            return;
        }

        await StartConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StartConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connection!.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnauthorized(ex))
        {
            var refreshed = await _authentication
                .TryRefreshAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!refreshed)
            {
                await _authentication.LogoutAsync().ConfigureAwait(false);
                throw;
            }

            _accessToken = _authentication.AccessToken;
            _connection = CreateConnection();
            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsUnauthorized(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            {
                return true;
            }
        }

        return false;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        if (_connection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
        {
            await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private HubConnection CreateConnection()
    {
        var hubUrl = new Uri(new Uri(_appOptions.ApiBaseAddress), "chatHub");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var token = await _authentication
                        .GetValidAccessTokenAsync()
                        .ConfigureAwait(false);
                    _accessToken = token;
                    return token;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<string, string, DateTimeOffset>("ReceiveMessage", (_, _, _) =>
        {
            MessageReceived?.Invoke(this, EventArgs.Empty);
        });

        return connection;
    }
}
