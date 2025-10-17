using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

public partial class Character : CharacterBody3D
{
    [Export] public CombatPlayer playerOwner;
    [Export] Startup startup;
    protected bool initialized = false;
    protected RingBuffer<TransformState> transformStates;
    protected RingBuffer<InputState> inputStates;
    protected ulong time;
    protected ulong lastTime;
    protected float defaultDelta;
    protected ulong tickRate;
    protected int currentTick;
    protected int gravity = 10;
    protected float speed = 2;
    protected bool owner = false;

    protected bool floored = false;

    //Server fields//
    Queue<InputState> clientInputs;

    //Client fields//
    protected InputState lastInputState;
    protected TransformState lastServerProcessState;
    
    public override void _Ready()
    {
        base._Ready();

        transformStates = new RingBuffer<TransformState>(1000);
        inputStates = new RingBuffer<InputState>(1000);
        currentTick = 0;
        tickRate = 16;
        defaultDelta = tickRate / 1000f;

        startup = GetNode<Startup>("/root/Main");
        Position = new Vector3I(0, 10, 0);

        if (Multiplayer.IsServer())
        {
            clientInputs = new Queue<InputState>();
            lastTime = Time.GetTicksMsec();
            initialized = true;
        }else
        {
            lastServerProcessState.serverReconcilated = true;
        }
    
        SetPhysicsProcess(true);
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        if (Multiplayer.IsServer())
        {
            SetMultiplayerAuthority((int)playerOwner.peerOwner,true);
        }
        else
        {
            RpcId(1, nameof(RequestPlayerAuthority));
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!initialized) return;

        ulong currentTime = playerOwner.serverTimer.GetServerTime();

        time += currentTime - lastTime;
        lastTime = currentTime;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (!initialized) return;

        ulong currentTime = playerOwner.serverTimer.GetServerTime();

        while (time >= tickRate)
        {
            time -= tickRate;

            if (Multiplayer.IsServer())
            {
                TickServer(defaultDelta, currentTick, currentTime, false);
            }
            else
            {
                TickClient(defaultDelta, currentTick, currentTime);
            }
            
            currentTick += 1;
        }
    }

    //Request Authority Player
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestPlayerAuthority()
    {
        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceivePlayerAuthority), playerOwner.peerOwner);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceivePlayerAuthority(long peerOwner)
    {
        playerOwner = startup.combatPlayers[peerOwner];
        SetMultiplayerAuthority((int)peerOwner, true);

        owner = peerOwner == Multiplayer.GetUniqueId();

        RpcId(1, nameof(RequestCharacterSpawnState));
    }

    //Request Character Spawn Reconciliation

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public async void RequestCharacterSpawnState()
    {
        await ToSignal(GetTree().CreateTimer(1f), SceneTreeTimer.SignalName.Timeout);
        TransformState currentState = new();

        bool got = transformStates.TryGetByTick(currentTick - 1, out currentState);

        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveCharacterSpawnState), currentState.ToDictionary());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveCharacterSpawnState(Dictionary transformDictionary)
    {
        SpawnReconciliation(TransformState.FromDictionary(transformDictionary));
    }

    //Inputs Replication

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveClientInput(Dictionary inputState)
    {
        clientInputs.Enqueue(InputState.FromDictionary(inputState));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceivedClientReconciliation(Dictionary transformDictionary, int lastTick)
    {
        lastServerProcessState = TransformState.FromDictionary(transformDictionary);
        lastServerProcessState.tickState.tick = lastTick;
        lastServerProcessState.serverReconcilated = false;
    }

    //Server Methods//
    public void TickServer(float delta, int currentTick, ulong currentTime, bool processingInputs)
    {
        TickGravity(delta);
        ProcessInputState(lastInputState);
        MoveAndSlide();
        SaveTick(currentTick, currentTime);

        if (!processingInputs)
        {
            ProcessClientInputs(delta);
        }
    }
    
    public void ProcessClientInputs(float delta)
    {
        TransformState currentTransform = new();
        transformStates.TryGetByTick(currentTick, out currentTransform);

        if (clientInputs.Count > 0)
        {
            InputState item = clientInputs.Dequeue();

            lastInputState = new InputState(item.inputDirection, item.inputDirectionFraction, item.jumped, currentTransform.tickState);

            TickServer(delta, currentTick, currentTransform.tickState.tickTimestamp, true);

            transformStates.TryGetByTick(currentTick, out currentTransform);

            RpcId(playerOwner.peerOwner, nameof(ReceivedClientReconciliation), currentTransform.ToDictionary(), currentTransform.tickState.tick);
        }
    }

    //Client Methods//
    public void TickClient(float delta, int currentTick, ulong currentTime)
    {
        TickGravity(delta);
        bool newInput = CheckInputs(delta, currentTime);
        ProcessInputState(lastInputState);
        MoveAndSlide();
        SaveTick(currentTick, currentTime);

        if (newInput)
        {
            RpcId(1, nameof(ReceiveClientInput), lastInputState.ToDictionary());
        }

        if (!lastServerProcessState.serverReconcilated)
        {
            Reconciliation();
        }
    }

    public void Reconciliation()
    {
        TransformState reconciliationState = new();

        transformStates.TryGetByTick(lastServerProcessState.tickState.tick, out reconciliationState);
        lastServerProcessState.serverReconcilated = true;

        if (reconciliationState.position.DistanceTo(lastServerProcessState.position) >= 0.01)
        {
            GD.Print(reconciliationState.position.DistanceTo(lastServerProcessState.position));

            //wll do later, but it shouldnt have reconcilations with 0 ms and a simple movement with no obstacles lol
        }
    }

    public bool CheckInputs(float delta, ulong currentTime)
    {
        bool newInput = false;

        if (!owner)
        {
            lastInputState = new InputState(Vector2I.Zero, Vector2I.Zero, false, new TickState(currentTick, currentTime));
            return newInput;
        }

        Vector2 inputDirectionFloat = Vector2.Zero;

        if (Input.IsActionPressed("move_forward"))
            inputDirectionFloat.Y -= 1;
        if (Input.IsActionPressed("move_backward"))
            inputDirectionFloat.Y += 1;
        if (Input.IsActionPressed("move_left"))
            inputDirectionFloat.X -= 1;
        if (Input.IsActionPressed("move_right"))
            inputDirectionFloat.X += 1;

        inputDirectionFloat = inputDirectionFloat.Normalized() * speed;

        Array<Vector2I> vectorsDeterministiced = GetDirectionVectorsDeterministiced(inputDirectionFloat);
        InputState newInputState = new InputState(vectorsDeterministiced[0], vectorsDeterministiced[1], false, new TickState(currentTick, currentTime));

        if (newInputState != lastInputState)
        {
            newInput = true;
        }

        lastInputState = newInputState;

        return newInput;
    }

    public void SpawnReconciliation(TransformState spawnTransformState)
    {
        ulong serverTime = playerOwner.serverTimer.GetServerTime();
        time = serverTime - spawnTransformState.tickState.tickTimestamp;

        Position = spawnTransformState.position;
        Velocity = spawnTransformState.velocity;

        lastInputState = new InputState(Vector2I.Zero, Vector2I.Zero, false, new TickState(currentTick, spawnTransformState.tickState.tickTimestamp));

        SaveTick(currentTick, spawnTransformState.tickState.tickTimestamp);

        lastTime = serverTime;
        initialized = true;

        currentTick += 1;
    }

    //Shared Methods//
    public void ProcessInputState(InputState input)
    {
        Vector2I inputDirection = input.inputDirection;
        Vector2I inputDirectionFraction = input.inputDirectionFraction;

        Vector2 moveDirection = VectorDeterministicedToFloatVector(inputDirection, inputDirectionFraction);
        Vector3 currentVelocity = Velocity;

        Velocity = new Vector3(moveDirection.X, currentVelocity.Y, moveDirection.Y);
    }

    public void TickGravity(float delta)
    {
        if (IsOnFloor()) return;

        Velocity += new Vector3(0, -gravity * delta, 0);
    }

    public void SaveTick(int currentTick, ulong currentTime)
    {
        transformStates.Push(currentTick, new TransformState(Velocity, Position, new TickState(currentTick, currentTime)));
        inputStates.Push(currentTick, lastInputState);

        if (!floored && IsOnFloor())
        {
            floored = true;

            if (Multiplayer.IsServer())
            {
                //GD.Print("--PrintServer--");
                //transformStates.PrintAll();
            }
            else
            {
                //GD.Print("--PrintClient--");
                //transformStates.PrintAll();
            }
        }
    }

    public Array<Vector2I> GetDirectionVectorsDeterministiced(Vector2 vector)
    {
        ulong integerX = (ulong)Math.Floor(vector.X);
        ulong integerY = (ulong)Math.Floor(vector.Y);

        ulong decimalX = (ulong)Math.Round((vector.X - integerX) * 1000);
        ulong decimalY = (ulong)Math.Round((vector.Y - integerY) * 1000);

        Vector2I integerVector = new((int)integerX, (int)integerY);
        Vector2I decimalVector = new((int)decimalX, (int)decimalY);

        return [integerVector, decimalVector];
    }
    
    public Vector2 VectorDeterministicedToFloatVector(Vector2I intVector, Vector2I decimalVector)
    {
        float x = (float)(intVector.X + (decimalVector.X / Math.Pow(10, 3)));
        float y = (float)(intVector.Y + (decimalVector.Y / Math.Pow(10, 3)));

        return new(x, y);
    }
}
