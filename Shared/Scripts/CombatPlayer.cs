using Godot;
using Godot.Collections;
using System;

public partial class CombatPlayer : Node
{
    [Export] public long peerOwner = 0;
    [Export] public Dictionary<string, Character> controllingCharacters;
    [Export] Startup startup;
    public Node charactersNode;

    public ServerTimer serverTimer;
    public bool startedSyncServerTimer = false;

    public override void _Ready()
    {
        base._Ready();

        startup = GetNode<Startup>("/root/Main");
        controllingCharacters = [];
        charactersNode = GetNode<Node>("/root/Main/CombatTest/Characters");

        serverTimer = new(this);

        if (!Multiplayer.IsServer())
        {
            RpcId(1, nameof(RequestPlayerAuthority));
        }
        else
        {
            Character mainCharacter = GD.Load<PackedScene>("res://Shared/Prefabs/character.tscn").Instantiate<Character>();
            mainCharacter.playerOwner = this;

            charactersNode.AddChild(mainCharacter);
            controllingCharacters.Add("Main", mainCharacter);
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        if (Multiplayer.IsServer())
        {
            SetMultiplayerAuthority((int)peerOwner,true);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!startedSyncServerTimer && !Multiplayer.IsServer() && peerOwner != 0 && peerOwner == Multiplayer.GetUniqueId())
        {
            startedSyncServerTimer = true;
            serverTimer.SyncServerTime();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestPlayerAuthority()
    {
        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceivePlayerAuthority), peerOwner);
    }
    
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceivePlayerAuthority(long peerOwner)
    {
        this.peerOwner = peerOwner;
        startup.combatPlayers.Add(peerOwner, this);
        SetMultiplayerAuthority((int)peerOwner,true);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void RequestServerTime(ulong clientSendTime)
    {
        double serverReceiveTime = Time.GetTicksMsec();

        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveServerTime), clientSendTime, serverReceiveTime);
    }
    
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveServerTime(ulong clientSendTime, ulong serverReceiveTime)
    {
        serverTimer.SetServerTimeOffset(clientSendTime, serverReceiveTime);
    }
}
