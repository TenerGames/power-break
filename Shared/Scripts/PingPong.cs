using Godot;
using System;
using System.Collections.Generic;

public partial class PingPong : Node
{
    [Export] public double PingInterval = 1.0;
    [Export] public float Smoothing = 0.2f;
    [Export] public int MaxSamples = 20;
    [Export] public float ClientSendRate = 60f;
    [Export] public float TickIntervalMs = 16.666f;

    private ClientStats clientStats;
    public long peerOwner = -1;

    private double _timeSinceLastPing = 0;
    private ulong _totalPings = 0;
    private ulong _totalPongs = 0;

    public ClientStats ClientStats
    {
        get { return clientStats; }
    }

    public override void _Ready()
    {
        clientStats = new ClientStats(-1);
    }

    public override void _EnterTree()
    {
        if (Multiplayer.IsServer() && peerOwner != -1)
            SetMultiplayerAuthority((int)peerOwner, true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer() || peerOwner == -1)
            return;

        _timeSinceLastPing += delta;
        if (_timeSinceLastPing >= PingInterval)
        {
            _timeSinceLastPing = 0;
            SendPing();
        }
    }

    private void SendPing()
    {
        EnsureClient();

        double now = Time.GetTicksMsec();
        clientStats.LastPingSentTime = now;
        clientStats.SentCount++;
        _totalPings++;

        RpcId(peerOwner, nameof(ClientOnServerPing), now);
    }

    private void EnsureClient()
    {
        if (clientStats.PeerId == -1)
            clientStats = new ClientStats((int)peerOwner);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ClientOnServerPing(double serverSentTime)
    {
        RpcId(1, nameof(ServerOnPong), serverSentTime);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ServerOnPong(double serverSentTime)
    {
        int sender = Multiplayer.GetRemoteSenderId();
    
        EnsureClient();

        clientStats.ReceivedCount++;
        _totalPongs++;

        double now = Time.GetTicksMsec();
        double newRtt = now - serverSentTime;

        clientStats.SmoothedRttMs = clientStats.SmoothedRttMs == 0
            ? newRtt
            : MathUtils.Lerp(clientStats.SmoothedRttMs, newRtt, Smoothing);

        clientStats.OneWayMs = clientStats.SmoothedRttMs * 0.5;

        clientStats.RecentRtts.Add(newRtt);
        if (clientStats.RecentRtts.Count > MaxSamples)
            clientStats.RecentRtts.RemoveAt(0);

        clientStats.JitterMs = Math.Clamp(CalculateJitter(clientStats.RecentRtts), 0, 500);
        clientStats.LossRate = Math.Clamp(1.0 - (clientStats.ReceivedCount / (double)clientStats.SentCount), 0, 1);

        /*
        GD.Print($"[Ping] Peer {sender} | RTT: {clientStats.SmoothedRttMs:F1}ms | " +
                 $"OneWay: {clientStats.OneWayMs:F1}ms | Jitter: {clientStats.JitterMs:F1}ms | " +
                 $"Loss: {clientStats.LossRate * 100:F1}% | " +
                 $"Buffer: {clientStats.BufferMs(ClientSendRate):F1}ms " +
                 $"({clientStats.BufferTicks(ClientSendRate, TickIntervalMs)} ticks)");
                 */
    }

    private double CalculateJitter(List<double> samples)
    {
        if (samples.Count < 2) return 0;
        double totalDiff = 0;
        for (int i = 1; i < samples.Count; i++)
            totalDiff += Math.Abs(samples[i] - samples[i - 1]);
        return totalDiff / (samples.Count - 1);
    }

    public int GetBufferTicksForPeer() =>
        clientStats.PeerId == -1 ? 2 : clientStats.BufferTicks(ClientSendRate, TickIntervalMs);

    public double GetLossRateForPeer() =>
        clientStats.PeerId == -1 ? 0 : clientStats.LossRate;
}

public class ClientStats
{
    public int PeerId = -1;
    public double SmoothedRttMs = 0;
    public double OneWayMs = 0;
    public double JitterMs = 0;
    public double LossRate = 0;
    public List<double> RecentRtts = new();
    public ulong SentCount = 0;
    public ulong ReceivedCount = 0;
    public double LastPingSentTime = 0;

    public ClientStats(int peerId) => PeerId = peerId;

    public double BufferMs(double sendRate)
    {
        double baseBuffer = OneWayMs + JitterMs + (1000.0 / sendRate);
        return baseBuffer * (1.0 + Math.Clamp(LossRate * 2.0, 0, 2));
    }

    public int BufferTicks(double sendRate, double tickMs) =>
        Mathf.RoundToInt((float)(BufferMs(sendRate) / tickMs));
}
