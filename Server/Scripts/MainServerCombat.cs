using Godot;
using Godot.Collections;
using System;

public partial class MainServerCombat : Node
{
    [Export] ENetMultiplayerPeer peer = null;
    [Export] Node playersNode;
    public Startup startup;
    private const int Port = 12345;

    public override void _Ready()
    {
        base._Ready();

        DisplayServer.WindowSetPosition(new Vector2I(1920 / 2, 100));
        DisplayServer.WindowSetSize(new Vector2I(960, 540)); 
        DisplayServer.WindowSetTitle("Server");

        startup = GetNode<Startup>("/root/Main");
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

        Error result = peer.CreateServer(Port, 32);

        if (result != Error.Ok)
        {
            return;
        }

        this.peer = peer;
        
        Multiplayer.MultiplayerPeer = this.peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    private void OnPeerConnected(long id)
    {
        CombatPlayer combatPlayer = GD.Load<PackedScene>("res://Shared/Prefabs/combat_player.tscn").Instantiate<CombatPlayer>();
        combatPlayer.peerOwner = id;

        playersNode.AddChild(combatPlayer);
        startup.combatPlayers.Add(id, combatPlayer);
    }
    
    private void OnPeerDisconnected(long id)
    {
        
    }
}
