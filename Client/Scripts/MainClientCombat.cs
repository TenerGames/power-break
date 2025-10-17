using Godot;
using Godot.Collections;
using System;

public partial class MainClientCombat : Node
{
    [Export] ENetMultiplayerPeer peer = null;
    private const int Port = 12345;
    private const string Address = "127.0.0.1";

    public override void _Ready()
    {
        base._Ready();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        TryStabilishConnection();
    }

    public void TryStabilishConnection()
    {
        if (this.peer != null) return;

        ENetMultiplayerPeer peer = new();

        Error result = peer.CreateClient(Address, Port);

        if (result != Error.Ok)
        {
            GD.Print("Error trying to connect to server");
            return;
        }

        this.peer = peer;
        
        Multiplayer.MultiplayerPeer = this.peer;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    private void OnConnectedToServer()
    {
        
    }

    private void OnServerDisconnected()
    {
        
    }
    
    private void OnConnectionFailed()
    {
        peer = null;

        TryStabilishConnection();
    }
}
