using System.Collections.Generic;
using Godot;
using Microsoft.Playfab.Gaming.GSDK.CSharp;

public class GameServer : Node {
    public const int ServerPort = 6575; // This is a random port, you can choose whatever you want.

    private NetworkedMultiplayerENet _network;
    private VBoxContainer _playersVBoxContainer;

    public override void _Ready() {
        base._Ready();

        _playersVBoxContainer = GetNode<VBoxContainer>("%PlayersVBoxContainer");
        GetNode<Button>("%AddClientButton").Connect("pressed", this, nameof(OnAddClientButtonPressed));

        if (this.InitializePlayfab()) {
            StartServer();
        }
    }

    private void OnAddClientButtonPressed() {
        var executableValue = OS.GetExecutablePath();
        var projectPath = ProjectSettings.GlobalizePath("res://");

        var args = new List<string>();
        args.Add("--path");
        args.Add(projectPath);
        // args.Add("--debug-collisions"); // This can be added to enable collisions on the client.
        args.Add("Scenes/GameClient.tscn");

        OS.Execute(executableValue, args.ToArray(), false);
    }

    private void StartServer() {
        GD.Print($"Starting the server on port {ServerPort}");

        _network = new NetworkedMultiplayerENet();
        _network.CreateServer(ServerPort, 10);
        GetTree().NetworkPeer = _network;

        _network.Connect("peer_connected", this, nameof(OnPeerConnected));
        _network.Connect("peer_disconnected", this, nameof(OnPeerDisconnected));
    }

    public void OnPeerConnected(int peerId) {
        GD.Print($"Peer connected: {peerId}");

        _playersVBoxContainer.AddChild(new Label() {
            Name = peerId.ToString(),
            Text = peerId.ToString()
        });
    }

    public void OnPeerDisconnected(int peerId) {
        // We are on the last player disconnected, we close the server
        if (this._playersVBoxContainer.GetChildCount() == 1) {
            this.GetTree().Quit();
        }

        _playersVBoxContainer.GetNode(peerId.ToString())?.QueueFree();
    }

    private bool InitializePlayfab() {
        try{
            GameserverSDK.RegisterShutdownCallback(() => {
                this.GetTree().Quit();
            });

            return GameserverSDK.ReadyForPlayers();
        } catch (Microsoft.Playfab.Gaming.GSDK.CSharp.GSDKInitializationException ex) {
            if (ex.Message == "GSDK file -  not found") {
                GD.Print("Starting the server without playfab");
                // Nothing to do, we are not on a container.
                return true;
            } else {
                throw ex;
            }
        }
    }
}
