using Godot;
using Godot.Collections;
using System;

public partial class CombatPlayer : Node
{
    [Export] public long peerOwner = 0;
    [Export] public Dictionary<string, Character> controllingCharacters;
    [Export] Startup startup;
    public Node pingsNode;
    public Node charactersNode;
    public bool isOwner = false;
    public PingPong pingPong;

    public override void _Ready()
    {
        base._Ready();

        startup = GetNode<Startup>("/root/Main");
        controllingCharacters = [];
        charactersNode = GetNode<Node>("/root/Main/CombatTest/Characters");
        pingsNode = GetNode<Node>("/root/Main/CombatTest/Pings");

        if (!Multiplayer.IsServer())
        {
            RpcId(1, nameof(RequestPlayerAuthority));
        }
        else
        {
            Character mainCharacter = GD.Load<PackedScene>("res://Shared/Prefabs/character.tscn").Instantiate<Character>();
            mainCharacter.playerOwner = this;

            mainCharacter.Name = "Character_" + peerOwner;

            charactersNode.AddChild(mainCharacter);
            controllingCharacters.Add("Main", mainCharacter);

            pingPong = GD.Load<PackedScene>("res://Shared/Prefabs/ping.tscn").Instantiate<PingPong>();
            pingPong.peerOwner = peerOwner;
            pingPong.Name = "PingPong_" + peerOwner;

            pingsNode.AddChild(pingPong);
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
        if (peerOwner == Multiplayer.GetUniqueId())
        {
            isOwner = true;
        }
        SetMultiplayerAuthority((int)peerOwner,true);
    }
}
