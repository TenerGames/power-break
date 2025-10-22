using System;
using System.Collections.Generic;
using System.Linq;
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
    public float jumpPower = 10.0f;
    public int currentTick = 0;
    public int minimunTickOffset = 50;
    public SimulationTypes characterSimulationType;
    public Queue<InputState> inputsQueue;

    // Client Vars //
    public TransformState? lastServerReconciliationState = default;
    public TransformState lastProcessedReconciliationState = default;
    public Godot.Collections.Dictionary<int,bool> inputsToConfirm;
    public Array<int> inputsConfirmed;
    public bool initilized = false;
    public int maxInputsToConfirm = 250;
    public int lastInputStartToRemove = 0;
    int lastInputConfirmed = -1;

    // Server Vars //
    public int? tickOffset = null;
    public int lastClientTickProcessed = -1;
    public TransformState? lastClientUpdateTransformState = null;
    public System.Collections.Generic.Dictionary<int, InputState> clientInputsToSimulate;
    public int clientCurrentInputToSimulate = -1;
    public int ignoreOne = 0;

    public RingBuffer<TransformState> TransformStates
    {
        get { return transformStates; }
    }

    public override void _Ready()
    {
        base._Ready();

        ulong currentTime = Time.GetTicksMsec();

        characterBody3D = this;

        inputsQueue = new Queue<InputState>();

        inputsConfirmed = [];
        inputsToConfirm = [];
        clientInputsToSimulate = [];
        transformStates = new RingBuffer<TransformState>(1000);
        inputStates = new RingBuffer<InputState>(1000);

        startup = GetNode<Startup>("/root/Main");
        tickDelta = 1.0f / Engine.PhysicsTicksPerSecond;

        Position = new Vector3(0, 1.425f, 0);

        if (Multiplayer.IsServer())
        {
            initilized = true;
        }

        SaveTick(currentTick, new InputState(default, default, false, new TickState(currentTick, currentTime)), currentTime, MovementSimulation.GetCharacterAttributes(this));
        SetPhysicsProcess(true);
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        if (Multiplayer.IsServer())
        {
            characterSimulationType = SimulationTypes.Server;
            SetMultiplayerAuthority((int)playerOwner.peerOwner, true);
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

        if (!initilized)
        {
            SaveTick(currentTick, new InputState(default, default, false, new TickState(currentTick, currentTime)), currentTime, MovementSimulation.GetCharacterAttributes(this));
            return;
        }

        switch (characterSimulationType)
        {
            case SimulationTypes.None:

                GD.Print("Character doenst have authority setted yet");
                break;

            case SimulationTypes.Server:
                TickServer(tickDelta, currentTick, currentTime);
                break;

            case SimulationTypes.ClientOwner:
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
            case SimulationTypes.None:
                GD.Print("Character doenst have authority setted yet");
                break;

            case SimulationTypes.Server:

                if (lastClientUpdateTransformState != null)
                {
                    RpcId(playerOwner.peerOwner, nameof(ReceiveServerReconcilationState), ((TransformState)lastClientUpdateTransformState).ToDictionary());
                }

                if (clientInputsToSimulate.Count > 250)
                {
                    int difference = clientInputsToSimulate.Count - 250;

                    GD.Print(clientInputsToSimulate.Count);

                    for (int i = 0; i < difference; i++)
                    {
                        clientInputsToSimulate.Remove(clientCurrentInputToSimulate);
                        clientCurrentInputToSimulate += 1;
                    }
                }

                break;

            case SimulationTypes.ClientOwner:
                while (inputsQueue.Count > 0)
                {
                    RpcId(1, nameof(SendCharacterOwnerInputs), inputsQueue.Dequeue().ToDictionary());
                }

                ReconstituteInputs();

                break;

            default:
                break;
        }
    }

    // Client Methods //
    public void ReconstituteInputs()
    {
        if (inputsToConfirm.Count == 0 || inputsConfirmed.Count == 0)
            return;

        Array<int> ticksNotConfirmed = [];
        Array<int> ticksConfirmed = [];

        for (int i = 0; i < inputsConfirmed.Count; i++)
        {
            int tick = inputsConfirmed[i];

            if (inputsToConfirm.TryGetValue(tick, out bool confirmedValue))
            {
                int expectedToConfirm = lastInputConfirmed + 1;

                if (tick > expectedToConfirm && lastInputConfirmed > -1)
                {
                    for (int tickUnconfirmed = expectedToConfirm; tickUnconfirmed < tick; tickUnconfirmed++)
                    {
                        inputsToConfirm.Remove(tickUnconfirmed);
                        ticksNotConfirmed.Add(tickUnconfirmed);
                    }
                }

                lastInputConfirmed = tick;
                inputsToConfirm.Remove(tick);
            }

            ticksConfirmed.Add(i);
        }

        for (int i = ticksConfirmed.Count - 1; i >= 0; i--)
        {
            int index = ticksConfirmed[i];

            inputsConfirmed.RemoveAt(index);
        }

        foreach(int tick in ticksNotConfirmed)
        {
            RpcId(1, nameof(SendCharacterOwnerReconstitutedInputs), inputStates.TryGetByTick(tick).ToDictionary());
        }
    }

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
            GD.Print("Distance from server position is ", distanceFromServer, " so lets reconcilate");

            Position = lastServerReconciliationState.position;
            Velocity = lastServerReconciliationState.velocity;

            InputState input = inputStates.TryGetByTick(reconcilationTick);
            TransformState oldState = transformStates.TryGetByTick(reconcilationTick);

            oldState.characterAttributesState.moveSpeed = lastServerReconciliationState.characterAttributesState.moveSpeed;
            oldState.characterAttributesState.jumpPower = lastServerReconciliationState.characterAttributesState.jumpPower;

            SaveTick(reconcilationTick, input, reconcilationState.tickState.tickTimestamp, oldState.characterAttributesState);

            reconcilationTick += 1;

            while (reconcilationTick <= processTick)
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

        MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta, ref transformState.characterAttributesState);

        SaveTick(processTick, input, transformState.tickState.tickTimestamp, transformState.characterAttributesState);
    }

    public void TickClientOwner(float delta, int processTick, ulong timeStamp)
    {
        InputState input = MovementSimulation.GetMovementInput(timeStamp, processTick);
        CharacterAttributesState characterAttributesState = MovementSimulation.GetCharacterAttributes(this);

        MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta, ref characterAttributesState);

        inputsQueue.Enqueue(input);
        inputsToConfirm.Add(processTick, false);

        if (inputsToConfirm.Count > maxInputsToConfirm)
        {
            int difference = inputsToConfirm.Count - maxInputsToConfirm;
            int startRemove = processTick - difference;

            for (int i = 0; i < difference; i++)
            {
                inputsToConfirm.Remove(startRemove);
                startRemove += 1;
            }
        }

        SaveTick(processTick, input, timeStamp, characterAttributesState);
        DoReconciliation(processTick);
    }

    // Server Methods //

    public void TickServer(float delta, int processTick, ulong timeStamp)
    {
        int currentTickOffset = playerOwner.pingPong.ClientStats.BufferTicks(playerOwner.pingPong.ClientSendRate, playerOwner.pingPong.TickIntervalMs, minimunTickOffset);

        if (tickOffset == null || currentTickOffset != tickOffset)
        {
            tickOffset = Math.Min(currentTickOffset, minimunTickOffset);
        }

        if (clientCurrentInputToSimulate > -1 && clientInputsToSimulate.TryGetValue(clientCurrentInputToSimulate, out InputState input))
        {
            int clientTick = clientCurrentInputToSimulate;
            ulong clientTimeStamp = input.tickState.tickTimestamp;
            int serverTick = clientTick + (int)tickOffset;
            bool cantProcess = false;

            if (!IsInputValid(ref input))
            {
                clientInputsToSimulate.Remove(clientCurrentInputToSimulate);
                clientCurrentInputToSimulate += 1;
                cantProcess = true;
            }

            if (clientTick < lastClientTickProcessed || processTick > serverTick)
            {
                clientInputsToSimulate.Remove(clientCurrentInputToSimulate);
                clientCurrentInputToSimulate += 1;
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

                CharacterAttributesState characterAttributesState = MovementSimulation.GetCharacterAttributes(this);
                MovementSimulation.SimulateCharacterFrame(this, ref input, processTick, delta, ref characterAttributesState);

                SaveTick(processTick, input, timeStamp, characterAttributesState);

                lastClientUpdateTransformState = MovementSimulation.GenerateTransformState(ref characterBody3D, clientTick, clientTimeStamp, characterAttributesState);

                clientInputsToSimulate.Remove(clientCurrentInputToSimulate);
                lastClientTickProcessed = clientCurrentInputToSimulate;
                clientCurrentInputToSimulate += 1;
                
                return;
            }
        }else if (clientCurrentInputToSimulate > -1) 
        {
            clientCurrentInputToSimulate += 1;
        }

        InputState emptyInput = default;

        emptyInput.tickState.tick = processTick;
        emptyInput.tickState.tickTimestamp = timeStamp;

        CharacterAttributesState newCharacterAttributesState = MovementSimulation.GetCharacterAttributes(this);
        MovementSimulation.SimulateCharacterFrame(this, ref emptyInput, processTick, delta, ref newCharacterAttributesState);

        SaveTick(processTick, emptyInput, timeStamp, newCharacterAttributesState);
    }
    
    public bool IsInputValid(ref InputState input)
    {
        bool valid = true;

        Vector2I inputDirection = input.inputDirection;

        if (inputDirection.X > 1 || inputDirection.X < -1 || inputDirection.Y > 1 || inputDirection.Y < -1)
        {
            valid = false;
        } 

        return valid;
    }

    // Shared Methods //
    public void SaveTick(int processTick, InputState input, ulong timeStamp, CharacterAttributesState characterAttributesState)
    {
        transformStates.Push(processTick, MovementSimulation.GenerateTransformState(ref characterBody3D, processTick, timeStamp, characterAttributesState));
        inputStates.Push(processTick, input);
    }

    public float GetCharacterGravity()
    {
        return Startup.GRAVITY;
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

        SetMultiplayerAuthority((int)peerOwner, true);

        if (peerOwner == Multiplayer.GetUniqueId())
        {
            characterSimulationType = SimulationTypes.ClientOwner;
        }
        else
        {
            characterSimulationType = SimulationTypes.Client;
        }

        initilized = true;
    }

    //Those rcps are to deal with inputs

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersInputsChannel)]
    public void SendCharacterOwnerInputs(Dictionary inputDictionary)
    {
        InputState newInput = InputState.FromDictionary(inputDictionary);

        if (GD.Randf() <= 0.10f)
        {
            //GD.Print("packte loss ", newInput.tickState.tick);
            //return; //lets ignore the client input
        }

        ignoreOne += 1;

        clientInputsToSimulate.Add(newInput.tickState.tick, newInput);

        if (clientCurrentInputToSimulate == -1 || newInput.tickState.tick < clientCurrentInputToSimulate)
        {
            clientCurrentInputToSimulate = newInput.tickState.tick;
        }

        RpcId(playerOwner.peerOwner, nameof(ConfirmedSentInput), newInput.tickState.tick);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersInputsChannel)]
    public void SendCharacterOwnerReconstitutedInputs(Dictionary inputDictionary)
    {
        InputState newInput = InputState.FromDictionary(inputDictionary);

        if (newInput.tickState.tick < lastClientTickProcessed) return;

        if (clientInputsToSimulate.TryGetValue(newInput.tickState.tick, out InputState _))
        {
            //GD.Print("server already have ", newInput.tickState.tick, " probaly packet loss");
            return;
        }

        clientInputsToSimulate.Add(newInput.tickState.tick, newInput);

        if (clientCurrentInputToSimulate == -1 || newInput.tickState.tick < clientCurrentInputToSimulate)
        {
            clientCurrentInputToSimulate = newInput.tickState.tick;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersInputsChannel)]
    public void ConfirmedSentInput(int inputConfirmed)
    {
        inputsConfirmed.Add(inputConfirmed);
    }

    //Those rcps are for client reconcilation

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = Startup.CharactersReconciliationChannel)]
    public void ReceiveServerReconcilationState(Dictionary transformDictionary)
    {
        lastServerReconciliationState = TransformState.FromDictionary(transformDictionary);
    }
}