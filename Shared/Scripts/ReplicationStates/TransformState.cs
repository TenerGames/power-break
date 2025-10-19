using Godot;
using Godot.Collections;

public struct TransformState(Vector3 velocity, Vector3 position, TickState tickState, bool isOnFloor)
{
    public Vector3 velocity = velocity;
    public Vector3 position = position;
    public TickState tickState = tickState;
    public bool serverReconcilated = true;
    public bool isOnFloor = isOnFloor;

    public readonly Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "velocity", velocity },
            { "position", position },
            { "tickState", tickState.ToDictionary() },
            { "isOnFloor", isOnFloor }
        };
    }

    public static TransformState FromDictionary(Dictionary dict)
    {
        Vector3 velocity = (Vector3)dict["velocity"];
        Vector3 position = (Vector3)dict["position"];
        TickState tickState = TickState.FromDictionary((Dictionary)dict["tickState"]);
        bool isOnFloor = (bool)dict["isOnFloor"];

        return new TransformState(velocity, position, tickState, isOnFloor);
    }

    public override string ToString()
    {
        return $"Pos: {position}, Vel: {velocity}, Tick: {tickState.tick}, TimeStamp: {tickState.tickTimestamp}";
    }
}
