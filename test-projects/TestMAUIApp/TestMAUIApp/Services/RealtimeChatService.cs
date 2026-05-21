using Microsoft.AspNetCore.SignalR.Client;

namespace TestMAUIApp.Services;

public class RealtimeChatService
{
    private HubConnection? _connection;
    private string? _accessToken;

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

        await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
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
        var hubUrl = new Uri(new Uri(MobileAppServices.ApiBaseAddress), "chatHub");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var token = await MobileAppServices.AuthenticationService
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
