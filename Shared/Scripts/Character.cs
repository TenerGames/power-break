using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

public partial class Character : CharacterBody3D
{
    [Export] public CombatPlayer playerOwner;
    [Export] Startup startup;
    [Export] public CollisionShape3D collisionShape3D;
    private CapsuleShape3D capsuleShape3D;
    private ShapeCast3D floorCast;
    protected RingBuffer<TransformState> transformStates;
    protected RingBuffer<InputState> inputStates;
    ulong tickRate;
    ulong time;
    ulong lastTime;
    int currentTick = 0;
    float speed = 2;
    protected float tickDelta;
    protected int tickOffset;
    protected bool initialized = false;
    protected float gravity = 20.0f;
    protected bool jumpedlol = false;
    protected float jumpPower = 200.0f;

    //Server Vars //
    Queue<InputState> clientInputs;
    InputState lastProcessedInput;
    protected int maxClientInputsAllowed = 20;

    //Client vars//
    TransformState lastReconciliationState;
    TransformState currentReconciliationState;

    public override void _Ready()
    {
        base._Ready();

        Position = new Vector3(0, 1.5f, 0);

        startup = GetNode<Startup>("/root/Main");
        tickRate = 16;
        tickDelta = 1f / 60f;
        lastTime = Time.GetTicksMsec();
        tickOffset = 0;
        capsuleShape3D = collisionShape3D.Shape as CapsuleShape3D;

        floorCast = new ShapeCast3D();
        clientInputs = new Queue<InputState>();
        transformStates = new RingBuffer<TransformState>(1000);
        inputStates = new RingBuffer<InputState>(1000);

        floorCast.Shape = new CapsuleShape3D
        {
            Height = capsuleShape3D.Height * 0.95F,
            Radius = capsuleShape3D.Radius * 0.95F
        };
        floorCast.TargetPosition = new Vector3(0, -0.2f, 0);
        AddChild(floorCast);
        floorCast.Enabled = true;

        SaveTick(currentTick, lastTime, new InputState(), true);

        if (Multiplayer.IsServer())
        {
            initialized = true;
        }

        SetPhysicsProcess(true);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        ulong currentTime = Time.GetTicksMsec();

        time += currentTime - lastTime;
        lastTime = currentTime;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        ulong currentTime = Time.GetTicksMsec();

        while (time >= tickRate)
        {
            time -= tickRate;
            currentTick += 1;

            if (Multiplayer.IsServer())
            {
                TickServer(tickDelta, currentTick, currentTime);
            }
            else
            {
                if (!initialized)
                {
                    SaveTick(currentTick, currentTime, new InputState(), true);
                }
                else
                {
                    TickClient(tickDelta, currentTick, currentTime);
                }
            }
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        if (Multiplayer.IsServer())
        {
            SetMultiplayerAuthority((int)playerOwner.peerOwner, true);
        }else
        {
            RpcId(1, nameof(ClientRequestedFirstData));
        }
    }

    // Client Methods //

    public InputState GetInputState(ulong timeStamp, int processTick)
    {
        Vector2 inputDirectionFloat = Vector2.Zero;
        bool jumped = false;

        if (Input.IsActionPressed("move_forward"))
            inputDirectionFloat.Y -= 1;
        if (Input.IsActionPressed("move_backward"))
            inputDirectionFloat.Y += 1;
        if (Input.IsActionPressed("move_left"))
            inputDirectionFloat.X -= 1;
        if (Input.IsActionPressed("move_right"))
            inputDirectionFloat.X += 1;
        if (Input.IsActionPressed("jump"))
            jumped = true;

        inputDirectionFloat = inputDirectionFloat.Normalized() * speed;

        Array<Vector2I> vectorsDeterministiced = GetDirectionVectorsDeterministiced(inputDirectionFloat);
        return new InputState(vectorsDeterministiced[0], vectorsDeterministiced[1], jumped, new TickState(processTick, timeStamp));
    }

    public void ReconciliationClientTick(float delta, int processTick, ulong timeStamp)
    {
        InputState inputState = GetInputState(timeStamp,processTick);
        ProcessInputState(inputState,processTick-1);
        SimulateGravity(delta,-1);
        MoveAndSlide();
        SaveTick(processTick, timeStamp, inputState, IsOnFloorDeterministic(-1));
    }

    public void TickClient(float delta, int processTick, ulong timeStamp)
    {
        DoReconciliation();
        InputState inputState = GetInputState(timeStamp,processTick);
        ProcessInputState(inputState,processTick-1);
        SimulateGravity(delta,processTick-1);
        MoveAndSlide();
        SaveTick(processTick, timeStamp, inputState, IsOnFloorDeterministic(-1));

        RpcId(1, nameof(ReceiveClientInput), inputState.ToDictionary());
    }

    public void DoReconciliation()
    {
        int reconciliationTick = currentReconciliationState.tickState.tick;

        if (reconciliationTick <= lastReconciliationState.tickState.tick) return;

        lastReconciliationState.tickState.tick = reconciliationTick;

        TransformState clientProcessedState = new();
        InputState clientProcessedInput = new();

        transformStates.TryGetByTick(reconciliationTick, out clientProcessedState);
        inputStates.TryGetByTick(reconciliationTick, out clientProcessedInput);

        if (clientProcessedState.position.DistanceTo(currentReconciliationState.position) > 0.01)
        {
            Position = currentReconciliationState.position;
            Velocity = currentReconciliationState.velocity;

            GD.Print("we have to reconciliate, position is different as f");
            GD.Print("Client was ", clientProcessedState.position);
            GD.Print("Client isOnFloor ", clientProcessedState.isOnFloor);
            GD.Print("Server was ", currentReconciliationState.position);
            GD.Print("Server isOnFloor ", currentReconciliationState.isOnFloor);

            SaveTick(reconciliationTick, currentReconciliationState.tickState.tickTimestamp, clientProcessedInput, currentReconciliationState.isOnFloor);

            reconciliationTick += 1;

            while (reconciliationTick < currentTick)
            {
                ReconciliationClientTick(tickDelta, reconciliationTick, currentReconciliationState.tickState.tickTimestamp);

                reconciliationTick += 1;
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveServerReconciliationState(Dictionary transformDictionary, int clientTick)
    {
        currentReconciliationState = TransformState.FromDictionary(transformDictionary);
        currentReconciliationState.tickState.tick = clientTick;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveFirstData(long peerOwner)
    {
        playerOwner = startup.combatPlayers[peerOwner];
        initialized = true;
    }

    // Server Methods //

    public int? ProcessClientOldInputs(float delta, int processTick, ulong timeStamp)
    {
        int inputsDifference = clientInputs.Count - maxClientInputsAllowed;

        if (inputsDifference > 0) //means player hacked somehow and sent a lot of inputs, lets clear 
        {
            for (int i = 0; i <= inputsDifference; i++)
            {
                clientInputs.Dequeue();
            }

            GD.Print("removed ", inputsDifference);
        }

        if (clientInputs.Count > 0)
        {
            InputState inputState = clientInputs.Dequeue();
            int clientTick = inputState.tickState.tick;

            if (clientTick < lastProcessedInput.tickState.tick)
            {
                GD.Print("Cant, wrong input order, probaly packte loss");
                return null;
            }

            lastProcessedInput.tickState.tick = clientTick;

            int tickDelay = playerOwner.pingPong.ClientStats.BufferTicks(playerOwner.pingPong.ClientSendRate, playerOwner.pingPong.TickIntervalMs);
            int serverTick = clientTick + tickDelay;
            int sendServerTick = serverTick;

            inputState.tickState.tick = serverTick;
            inputState.tickState.tickTimestamp = timeStamp;

            inputStates.Push(serverTick, inputState);

            if (serverTick == processTick)
            {
                GD.Print("same input");
                return clientTick;
            }

            TransformState transformStateInputed = new();
            transformStates.TryGetByTick(serverTick - 1, out transformStateInputed);

            Position = transformStateInputed.position;
            Velocity = transformStateInputed.velocity;

            while (serverTick < processTick)
            {
                TransformState transformStateRollback = new();
                transformStates.TryGetByTick(serverTick, out transformStateRollback);

                TickServerRollback(tickDelta, serverTick, transformStateRollback.tickState.tickTimestamp);
                serverTick += 1;
            }

            TransformState reconciliationState = new();
            transformStates.TryGetByTick(sendServerTick, out reconciliationState);

            RpcId(playerOwner.peerOwner, nameof(ReceiveServerReconciliationState), reconciliationState.ToDictionary(), clientTick);

            return null;
        }
        else
        {
            InputState inputState = new(Vector2I.Zero, Vector2I.Zero, false, new TickState(processTick, timeStamp));

            inputStates.Push(processTick, inputState);
        }

        return null;
    }

    public void TickServerRollback(float delta, int processTick, ulong timeStamp)
    {
        InputState inputState = new();
        inputStates.TryGetByTick(processTick, out inputState);
        ProcessInputState(inputState,processTick-1);
        SimulateGravity(delta,-1);
        MoveAndSlide();
        SaveTick(processTick, timeStamp, inputState, IsOnFloorDeterministic(-1));
    }

    public void TickServer(float delta, int processTick, ulong timeStamp)
    {
        int? clientSendTick = ProcessClientOldInputs(delta, processTick, timeStamp);
        InputState inputState = new();
        inputStates.TryGetByTick(processTick, out inputState);
        ProcessInputState(inputState,processTick-1);
        SimulateGravity(delta,processTick-1);
        MoveAndSlide();
        SaveTick(processTick, timeStamp, inputState, IsOnFloorDeterministic(-1));

        if (clientSendTick != null)
        {
            TransformState reconciliationState = new();
            transformStates.TryGetByTick(processTick, out reconciliationState);

            RpcId(playerOwner.peerOwner, nameof(ReceiveServerReconciliationState), reconciliationState.ToDictionary(), (int)clientSendTick);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveClientInput(Dictionary inputDictionary)
    {
        clientInputs.Enqueue(InputState.FromDictionary(inputDictionary));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientRequestedFirstData()
    {
        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveFirstData), playerOwner.peerOwner);
    }

    // Shared Methods //

    public bool IsOnFloorDeterministic(int processTick)
    {
        if (processTick == -1)
        {
            return IsOnFloor();
        }else
        {
            TransformState processTransformState = new();
            bool hasState = transformStates.TryGetByTick(processTick, out processTransformState);

            if (hasState)
            {
                return processTransformState.isOnFloor;
            }

            return IsOnFloor();
        }
    }

    public void SimulateGravity(float delta,int processTick)
    {
        bool isOnFloor = false;

        if (processTick == -1)
        {
            isOnFloor = IsOnFloorDeterministic(-1);

            if (Multiplayer.IsServer())
            {
                //GD.Print("Server floor ", isOnFloor);
                //GD.Print("Server position ", Position);
            }
        }
        else
        {
            TransformState oldProcessTransformState = new();
            transformStates.TryGetByTick(processTick, out oldProcessTransformState);

            isOnFloor = oldProcessTransformState.isOnFloor;
        }

        if (isOnFloor) return;

        Vector3 currentVelocity = Velocity;

        currentVelocity += new Vector3(currentVelocity.X, -gravity * delta, currentVelocity.Z);

        Velocity = currentVelocity;
    }

    public void SaveTick(int processTick,ulong timeStamp,InputState inputState,bool isOnFloor)
    {
        transformStates.Push(processTick, new TransformState(Velocity, Position, new TickState(processTick, timeStamp), isOnFloor));
        inputStates.Push(processTick, inputState);
    }

    public void ProcessInputState(InputState input, int processTick)
    {
        TransformState oldProcessTransformState = new();
        transformStates.TryGetByTick(processTick, out oldProcessTransformState);

        Vector2I inputDirection = input.inputDirection;
        Vector2I inputDirectionFraction = input.inputDirectionFraction;

        Vector2 moveDirection = VectorDeterministicedToFloatVector(inputDirection, inputDirectionFraction);
        Vector3 currentVelocity = Velocity;

        bool isOnFloor = oldProcessTransformState.isOnFloor;

        if (!Multiplayer.IsServer() && input.jumped)
        {
            //GD.Print(isOnFloor);
        }

        float y = input.jumped && isOnFloor ? currentVelocity.Y + (jumpPower * tickDelta) : currentVelocity.Y;

        Velocity = new Vector3(moveDirection.X, y, moveDirection.Y);
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
