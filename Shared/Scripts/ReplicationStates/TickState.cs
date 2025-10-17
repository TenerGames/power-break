using Godot.Collections;

public struct TickState(int tick, ulong tickTimestamp)
{
    public int tick = tick;
    public ulong tickTimestamp = tickTimestamp;

    public readonly Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "tick", tick },
            { "tickTimestamp", tickTimestamp },
        };
    }

    public static TickState FromDictionary(Dictionary data)
    {
        return new TickState((int)data["tick"], (ulong)data["tickTimestamp"]);
    }
}
