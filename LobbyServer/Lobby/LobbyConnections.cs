using System.Collections.Concurrent;
using System.Net.WebSockets;

public class LobbyConnections {
    private readonly ConcurrentDictionary<WebSocket, LobbyClient> _connections = new ConcurrentDictionary<WebSocket, LobbyClient>();

    public ConcurrentDictionary<WebSocket, LobbyClient> Connections {
        get {
            return this._connections;
        }
    }
}
