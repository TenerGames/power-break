using Godot;
using System;

public class ServerTimer
{
    private long serverTimeOffset = 0;
    private long smoothedOffset = 0;
    private const float alpha = 0.1f;
    private readonly CombatPlayer combatPlayer;

    public ServerTimer(CombatPlayer combatPlayer)
    {
        this.combatPlayer = combatPlayer;
    }

    public void SyncServerTime()
    {
        combatPlayer.RpcId(1, nameof(combatPlayer.RequestServerTime), Time.GetTicksMsec());
        combatPlayer.GetTree().CreateTimer(0.1).Timeout += SyncServerTime;
    }

    public void SetServerTimeOffset(ulong clientSendTime, ulong serverReceiveTime)
    {
        ulong clientReceiveTime = Time.GetTicksMsec();
        long rtt = (long)(clientReceiveTime - clientSendTime);

        long newOffset = (long)serverReceiveTime - ((long)clientSendTime + (rtt / 2));
        serverTimeOffset = Math.Max(newOffset, 0);

        //GD.Print(serverTimeOffset);
    }

    public ulong GetServerTime()
    {
        return (ulong)((long)Time.GetTicksMsec() + serverTimeOffset);
    }
}

