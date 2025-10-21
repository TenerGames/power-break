using System.Collections.Generic;
using Godot;
using Godot.Collections;

public partial class Character : CharacterBody3D
{
    public CombatPlayer playerOwner;
    Startup startup;
    protected RingBuffer<TransformState> transformStates;
    protected RingBuffer<InputState> inputStates;
    CharacterBody3D characterBody3D;
    float tickDelta;
    public float moveSpeed = 10.0f;
    public float gravity = 20.0f;
    public float jumpPower = 10.0f;
    public int currentTick = 0;
    public int minimunTickOffset = 50;
    public enum CharacterSimulationTypes
    {
        None,
        ClientOwner,
        Server,
        Client,
    }
    public CharacterSimulationTypes characterSimulationType;
    public Queue<InputState> inputsQueue;

    // Client Vars //
    public TransformState? lastServerReconciliationState = default;
    public TransformState lastProcessedReconciliationState = default;

    // Server Vars //
    public int? tickOffset = null;
    public int lastClientTickProcessed = -1;
    public TransformState? lastClientUpdateTransformState = null;

    public override void _Ready()
    {
        base._Ready();

        ulong currentTime = Time.GetTicksMsec();

        characterBody3D = this;

        inputsQueue = new Queue<InputState>();
        transformStates = new RingBuffer<TransformState>(1000);
        inputStates = new RingBuffer<InputState>(1000);

        startup = GetNode<Startup>("/root/Main");
        tickDelta = 1.0f / Engine.PhysicsTicksPerSecond;

        Position = new Vector3(0, 1.5f, 0);

        SaveTick(currentTick, new InputState(default, default, false, new TickState(currentTick, currentTime)), currentTime);
        SetPhysicsProcess(true);
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        if (Multiplayer.IsServer())
        {
            characterSimulationType = CharacterSimulationTypes.Server;
            SetMultiplayerAuthority((int)playerOwner.peerOwner);
        }
        else
        {
            RpcId(1, nameof(RequestCharacterStartData));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        ulong currentTime = Time.GetTicksMsec();

        currentTick += 1;

        switch (characterSimulationType)
        {
            case CharacterSimulationTypes.None:

                GD.Print("Character doenst have authority setted yet");
                break;

            case CharacterSimulationTypes.Server:
                TickServer(tickDelta, currentTick, currentTime);
                break;

            case CharacterSimulationTypes.ClientOwner:
                TickClientOwner(tickDelta, currentTick, currentTime);
                break;

            default:
                break;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        switch (characterSimulationType)
        {
            case CharacterSimulationTypes.None:
                GD.Print("Character doenst have authority setted yet");
                break;

            case CharacterSimulationTypes.Server:

                if (lastClientUpdateTransformState != null)
                {
                    RpcId(playerOwner.peerOwner, nameof(ReceiveServerReconcilationState), ((TransformState)lastClientUpdateTransformState).ToDictionary());
                }

                break;

            case CharacterSimulationTypes.ClientOwner:
                while (inputsQueue.Count > 0)
                {
                    RpcId(1, nameof(SendCharacterOwnerInputs), inputsQueue.Dequeue().ToDictionary());
                }
                break;

            default:
                break;
        }
    }

    // Client Methods //

    public void DoReconciliation(int processTick)
    {
        if (this.lastServerReconciliationState == null) return;

        TransformState lastServerReconciliationState = (TransformState)this.lastServerReconciliationState;
        int reconcilationTick = lastServerReconciliationState.tickState.tick;

        if (reconcilationTick < lastProcessedReconciliationState.tickState.tick) return;

        lastProcessedReconciliationState.tickState.tick = processTick;

        TransformState reconcilationState = transformStates.TryGetByTick(reconcilationTick);
        float distanceFromServer = reconcilationState.position.DistanceTo(lastServerReconciliationState.position);

        if (distanceFromServer >= 0.01f)
        {
            GD.Print("Distance from server position is ", distanceFromServer);

            Position = lastServerReconciliationState.position;
            Velocity = lastServerReconciliationState.velocity;

            InputState input = inputStates.TryGetByTick(reconcilationTick);

            SaveTick(reconcilationTick, input, reconcilationState.tickState.tickTimestamp);

            reconcilationTick += 1;

            while (reconcilationTick < processTick)
            {
                TickClientOwnerReconciliation(tickDelta, reconcilationTick);

                reconcilationTick += 1;
            }
        }
    }
    
    public void TickClientOwnerReconciliation(float delta, int processTick)
    {
        InputState input = inputStates.TryGetByTick(processTick);
        TransformState transformState = transformStates.TryGetByTick(processTick);

        MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta);

        SaveTick(processTick, input, transformState.tickState.tickTimestamp);
    }

    public void TickClientOwner(float delta, int processTick, ulong timeStamp)
    {
        InputState input = MovementSimulation.GetMovementInput(timeStamp, processTick, moveSpeed);
        MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta);

        inputsQueue.Enqueue(input);

        SaveTick(processTick, input, timeStamp);
        DoReconciliation(processTick);
    }

    // Server Methods //

    public void TickServer(float delta, int processTick, ulong timeStamp)
    {
        int currentTickOffset = playerOwner.pingPong.ClientStats.BufferTicks(playerOwner.pingPong.ClientSendRate, playerOwner.pingPong.TickIntervalMs, minimunTickOffset);

        if (tickOffset == null || currentTickOffset > tickOffset)
        {
            tickOffset = currentTickOffset;
        }

        if (inputsQueue.Count > 0)
        {
            InputState input = inputsQueue.Peek();

            int clientTick = input.tickState.tick;
            ulong clientTimeStamp = input.tickState.tickTimestamp;
            int serverTick = clientTick + (int)tickOffset;
            bool cantProcess = false;

            if (clientTick < lastClientTickProcessed || serverTick < processTick)
            {
                inputsQueue.Dequeue();
                cantProcess = true;
            }

            if (serverTick < processTick)
            {
                cantProcess = true;
            }

            if (!cantProcess)
            {
                input.tickState.tick = processTick;
                input.tickState.tickTimestamp = timeStamp;

                MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta);
                SaveTick(processTick, input, timeStamp);

                lastClientUpdateTransformState = MovementSimulation.GenerateTransformState(ref characterBody3D, clientTick, clientTimeStamp);

                inputsQueue.Dequeue();
                return;
            }
        }
       
        InputState emptyInput = new();
        MovementSimulation.SimulateCharacterFrame(this, ref emptyInput, processTick, delta);
             
        SaveTick(processTick, emptyInput, timeStamp);
    }

    // Shared Methods //
    public void SaveTick(int processTick, InputState input, ulong timeStamp)
    {
        transformStates.Push(processTick, MovementSimulation.GenerateTransformState(ref characterBody3D, processTick, timeStamp));
        inputStates.Push(processTick, input);
    }

    // RPCS //
    
    //Those rcps are for the client get the start character data from server

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = Startup.CharacterStartDataChannel)]
    public void RequestCharacterStartData()
    {
        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveCharacterStartData), playerOwner.peerOwner);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = Startup.CharacterStartDataChannel)]
    public void ReceiveCharacterStartData(long peerOwner)
    {
        playerOwner = startup.combatPlayers[peerOwner];

        if (peerOwner == Multiplayer.GetUniqueId())
        {
            characterSimulationType = CharacterSimulationTypes.ClientOwner;
        }
        else
        {
            characterSimulationType = CharacterSimulationTypes.Client;
        }
    }

    //Those rcps are to deal with inputs
    
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersInputsChannel)]
    public void SendCharacterOwnerInputs(Dictionary inputDictionary)
    {
        inputsQueue.Enqueue(InputState.FromDictionary(inputDictionary));
    }

    //Those rcps are for client reconcilation

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersReconciliationChannel)]
    public void ReceiveServerReconcilationState(Dictionary transformDictionary)
    {
        lastServerReconciliationState = TransformState.FromDictionary(transformDictionary);
    }
}