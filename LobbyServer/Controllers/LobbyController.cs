using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Text.Json.Nodes;
using PlayFab;
using PlayFab.MultiplayerModels;

[ApiController]
[Route("[controller]")]
public class LobbyController : ControllerBase {
    private const string SteamAuthUrl = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1";
    private const string SteamPlayerSummaryUrl = "https://partner.steam-api.com/ISteamUser/GetPlayerSummaries/v2/";
    private const string SteamAppIdParamName = "SteamAppId";
    private const string SteamPrivateKeyParamName = "SteamPrivateKey";
    private const string PlayfabTitleIdParamName = "PlayfabTitleId";
    private const string PlayfabSecretParamName = "PlayfabSecret";
    private const string PlayfabBuildIdParamName = "PlayfabBuildId";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LobbyController> _logger;
    private readonly LobbyConnections _lobbyConnections;

    public LobbyController(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<LobbyController> logger, LobbyConnections lobbyConnections) {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _lobbyConnections = lobbyConnections;
    }

    [HttpGet("/lobby")]
    public async Task Get() {
        if (HttpContext.WebSockets.IsWebSocketRequest) {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var client = await AuthenticateRequest(HttpContext.Request);

            if (client == null) {
                var closeMessage = new {
                    MessageType = ServerMessageType.ConnectionError,
                    MessageContent = new ConnectionErrorMessage() {
                        Reason = "Player not allowed"
                    }
                };
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, JsonSerializer.Serialize(closeMessage), CancellationToken.None);
            } else {
                _logger.Log(LogLevel.Information, "WebSocket connection established");
                _lobbyConnections.Connections.TryAdd(webSocket, client);

                await HandleWebSocketConnection(webSocket, client);
            }
        } else {
            HttpContext.Response.StatusCode = 400;
        }
    }

    private async Task HandleWebSocketConnection(WebSocket webSocket, LobbyClient client) {
        await SendMessageToConnection(webSocket, new {
            MessageType = ServerMessageType.ConnectionEstablished,
            MessageContent = new ConnectionEstablishedMessage() {
                Username = client.Username,
                WelcomeMessage = "Hey, welcome mate!"
            }
        });
        _logger.Log(LogLevel.Information, "Welcome message sent");

        var buffer = new byte[1024 * 4];
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        while (!result.CloseStatus.HasValue) {
            await HandleMessage(webSocket, client, buffer);

            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        }

        await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        _logger.Log(LogLevel.Information, "WebSocket connection closed");
        _lobbyConnections.Connections.TryRemove(webSocket, out client!);
    }

    private async Task<LobbyClient?> AuthenticateRequest(HttpRequest request) {
        StringValues steamTicket;
        if (!request.Headers.TryGetValue("x-steam-token", out steamTicket)) {
            return null;
        }

        var client = await GetClientFromSteamToken(steamTicket.ToString());
        if (client == null) {
            return null;
        }

        if (client.IsBan) {
            return null;
        }

        // Here we could validate the player against a Database

        return client;
    }

    private async Task<LobbyClient?> GetClientFromSteamToken(string ticket) {
        var url = $"{SteamAuthUrl}?key={_configuration[SteamPrivateKeyParamName]}&appid={_configuration[SteamAppIdParamName]}&ticket={ticket}";

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

        if (response.IsSuccessStatusCode) {
            var deserializeOption = new JsonSerializerOptions() {
                PropertyNameCaseInsensitive = true
            };

            using var contentStream = await response.Content.ReadAsStreamAsync();

            var loginResult = await JsonSerializer.DeserializeAsync<SteamUserInfoResponse>(contentStream, deserializeOption);
            if (loginResult?.Response?.Params?.Result == "OK") {
                var playerSummariesUrl = $"{SteamPlayerSummaryUrl}?key={_configuration[SteamPrivateKeyParamName]}&steamids={loginResult?.Response.Params.Steamid}";
                var playerSummariesResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, playerSummariesUrl));

                if (playerSummariesResponse.IsSuccessStatusCode) {
                    using var playerSummariesContentStream = await playerSummariesResponse.Content.ReadAsStreamAsync();
                    var playerSummariesResult = await JsonSerializer.DeserializeAsync<SteamPlayerSummariesResponse>(playerSummariesContentStream, deserializeOption);

                    return new LobbyClient() {
                        Username = playerSummariesResult?.Response?.Players?[0]?.Personaname,
                        AvatarUrl = playerSummariesResult?.Response?.Players?[0]?.Avatar,
                        Steamid = loginResult?.Response.Params.Steamid,
                        IsBan = loginResult!.Response.Params.Vacbanned || loginResult!.Response.Params.Publisherbanned
                    };
                }
            }
        }

        return null;
    }

    private async Task SendMessageToConnection(WebSocket webSocket, dynamic message) {
        var messageContent = JsonSerializer.Serialize(message);
        var serverMsg = Encoding.UTF8.GetBytes(messageContent);
        await webSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task HandleMessage(WebSocket webSocket, LobbyClient client, byte[] buffer) {
        var message = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
        var messageData = JsonSerializer.Deserialize<JsonObject>(message);

        switch ((ClientMessageType) (int) messageData!["MessageType"]!) {
            case ClientMessageType.StartMatchMaking:
                await SendMessageToConnection(webSocket, new {
                    MessageType = ServerMessageType.MatchmakingStarted
                });

                client.IsMatchmaking = true;

                await this.TryFindMatch(webSocket, client);

                break;
        }
    }

    private async Task TryFindMatch(WebSocket sourceWebSocket, LobbyClient sourceClient) {
        var otherPlayer = this._lobbyConnections.Connections.FirstOrDefault(keyValue => keyValue.Value != sourceClient && keyValue.Value.IsMatchmaking);

        if ((otherPlayer.Key, otherPlayer.Value) != default) {
            sourceClient.IsMatchmaking = false;
            otherPlayer.Value.IsMatchmaking = false;

            var session = await this.DeployOnPlayfab();
            if (session != null && session.Result != null) {
                var matchFoundMessage = new {
                    MessageType = ServerMessageType.MatchFound,
                    MessageContent = new MatchFoundMessage() {
                        ServerUrl = session.Result.FQDN,
                        ServerPort = session.Result.Ports.First(port => port.Name == "gameport")?.Num
                    }
                };

                await SendMessageToConnection(sourceWebSocket, matchFoundMessage);
                await SendMessageToConnection(otherPlayer.Key, matchFoundMessage);

                _logger.Log(LogLevel.Information, $"Match found for {sourceClient.Username} and {otherPlayer.Value.Username} on the server {session.Result.FQDN}");
            }
        }
    }

    private async Task<PlayFabResult<RequestMultiplayerServerResponse>> DeployOnPlayfab() {
        PlayFabSettings.staticSettings.DeveloperSecretKey = _configuration[PlayfabSecretParamName];
        PlayFabSettings.staticSettings.TitleId = _configuration[PlayfabTitleIdParamName];

        var entityToken = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new PlayFab.AuthenticationModels.GetEntityTokenRequest());
        if (entityToken.Error != null) {
            _logger.Log(LogLevel.Information, "Could not authenticate to playfab");
        } else {
            return await PlayFabMultiplayerAPI.RequestMultiplayerServerAsync(new PlayFab.MultiplayerModels.RequestMultiplayerServerRequest() {
                BuildId = _configuration[PlayfabBuildIdParamName],
                PreferredRegions = new[] {"EastUs"}.ToList(),
                SessionId = Guid.NewGuid().ToString()
            });
        }

        return null;
    }
}
