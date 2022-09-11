using Godot;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public class GameClient : Node {
    private const string ServerHost = "localhost";
    private const string LobbyServerHost = "wss://localhost:7218/lobby";

    private NetworkedMultiplayerENet _network;
    private Steamworks.AuthTicket _authTicket;
    private Label _statusLabel;
    private WebSocketClient _wsClient;

    public override void _Ready() {
        base._Ready();

        this._statusLabel = this.GetNode<Label>("%StatusLabel");
        this.GetNode<Button>("%ReadyButton").Connect("pressed", this, nameof(this.OnReadyButton));

        try {
            Steamworks.SteamClient.Init(SteamUtils.AppId);

            this._authTicket = Steamworks.SteamUser.GetAuthSessionTicket();
            var ticket = string.Concat(this._authTicket.Data.Select(x => x.ToString("X2")));

            this.ConnectToLobbyServer(ticket);
        } catch(Exception ex) {
            OS.Alert(ex.Message);
            this._statusLabel.Text = ex.Message;
        }
    }

    public override void _ExitTree() {
        base._ExitTree();

        this._authTicket?.Cancel();
        Steamworks.SteamClient.Shutdown();
    }

    private void ConnectToServer(string serverHost, int serverPort) {
        GD.Print($"Connecting to server {serverHost}:{serverPort}");

        _network = new NetworkedMultiplayerENet();
        _network.CreateClient(serverHost, serverPort);
        GetTree().NetworkPeer = _network;

        _network.Connect("connection_succeeded", this, nameof(OnConnectedToServer));
        _network.Connect("connection_failed", this, nameof(OnConnectionToServerFailed));
    }

    private void OnConnectedToServer() {
        GD.Print("Connected to server!");

        GetNode<Label>("%PeerIdLabel").Text = _network.GetUniqueId().ToString();
    }

    private void OnConnectionToServerFailed() {
        GD.Print("Failed to connect to server!");
    }

    private void ConnectToLobbyServer(string ticket) {
        this._wsClient = new WebSocketClient();
        this._wsClient.VerifySsl = false; // This line should be set to true if you actually connect to a server with a valid certificate.
        this._wsClient.Connect("connection_established", this, nameof(this.OnConnectionEstablished));
        this._wsClient.Connect("connection_error", this, nameof(this.OnConnectionError));
        this._wsClient.Connect("connection_closed", this, nameof(this.OnConnectionClosed));
        this._wsClient.Connect("data_received", this, nameof(this.OnDataReceived));
        this._wsClient.Connect("server_close_request", this, nameof(this.OnServerCloseRequest));

        this._wsClient.ConnectToUrl(LobbyServerHost, customHeaders: new string[] {$"x-steam-token: {ticket}"});
    }

    public override void _Process(float delta) {
        base._Process(delta);

        this._wsClient?.Poll();
    }

    private void OnReadyButton() {
        this.SendData(new {
            MessageType = ClientMessageType.StartMatchMaking
        });
    }

    private void OnConnectionEstablished(string protocol) {
        GD.Print($"OnConnectionEstablished {protocol}");
    }

    private void OnDataReceived() {
        var peer = this._wsClient.GetPeer(1);
        var packet = peer.GetPacket();
        bool isString = peer.WasStringPacket();

        if (isString) {
            var message = packet.GetStringFromUTF8();
            this.HandleMessageReceived(message);
        }
    }

    private void OnConnectionError() {
        GD.Print("OnConnectionError");
    }

    private void OnConnectionClosed(bool wasCleanClose) {
        GD.Print("OnConnectionClosed");
    }

    private void OnServerCloseRequest(int code, string reason) {
        GD.Print($"OnServerCloseRequest {code} {reason}");
        this._statusLabel.Text = $"OnServerCloseRequest {code} {reason}";
    }

    private Error SendData(dynamic message) {
        var messageContent = JsonSerializer.Serialize(message);
        return this._wsClient.GetPeer(1).PutPacket(Encoding.UTF8.GetBytes(messageContent));
    }

    private void HandleMessageReceived(string message) {
        var messageData = JsonSerializer.Deserialize<JsonObject>(message);
        switch ((ServerMessageType) (int) messageData["MessageType"]) {
            case ServerMessageType.ConnectionEstablished:
                var messageContent = messageData["MessageContent"].Deserialize<ConnectionEstablishedMessage>();
                this._statusLabel.Text = $"We are connected on the lobby server! {messageContent.Username}";

                break;
            case ServerMessageType.MatchmakingStarted:
                this._statusLabel.Text = $"Matchmaking started!";

                break;
            case ServerMessageType.MatchFound:
                this._statusLabel.Text = $"Hey match found!";
                var matchFoundMessage = messageData["MessageContent"].Deserialize<MatchFoundMessage>();
                this.ConnectToServer(matchFoundMessage.ServerUrl, matchFoundMessage.ServerPort);

                break;
        }
    }
}
