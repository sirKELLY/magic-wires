namespace EnergyFlow.Core
{
    /// <summary>
    /// Base contract for all simulation events (spec 7.5). Events are observation
    /// only: subscribing or not subscribing must have zero effect on the simulation.
    /// </summary>
    public interface ISimEvent
    {
        string NodeId { get; }
        long Tick { get; }
    }

    /// <summary>A source produced flow this tick (spec 7.5.1).</summary>
    public readonly struct SourceProduced : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public double Amount { get; }

        public SourceProduced(string nodeId, double amount, long tick)
        {
            NodeId = nodeId;
            Amount = amount;
            Tick = tick;
        }
    }

    /// <summary>A node received flow this tick. Drives edge-flow animation (spec 7.5.1).</summary>
    public readonly struct FlowDelivered : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public double Amount { get; }

        public FlowDelivered(string nodeId, double amount, long tick)
        {
            NodeId = nodeId;
            Amount = amount;
            Tick = tick;
        }
    }

    /// <summary>A gate discharged one packet of exactly its threshold (spec 7.5.1).</summary>
    public readonly struct GateDischarged : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public double Amount { get; }

        public GateDischarged(string nodeId, double amount, long tick)
        {
            NodeId = nodeId;
            Amount = amount;
            Tick = tick;
        }
    }

    /// <summary>
    /// One discrete sink fire. A sink crossing its cost twice in one tick emits
    /// two of these (spec 7.5.1).
    /// </summary>
    public readonly struct SinkFired : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public double Amount { get; }

        public SinkFired(string nodeId, double amount, long tick)
        {
            NodeId = nodeId;
            Amount = amount;
            Tick = tick;
        }
    }

    /// <summary>A switch changed state, effective on the given tick (spec 7.5.1).</summary>
    public readonly struct SwitchToggled : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public bool Open { get; }

        public SwitchToggled(string nodeId, bool open, long tick)
        {
            NodeId = nodeId;
            Open = open;
            Tick = tick;
        }
    }

    /// <summary>A node banked flow it could not push downstream (spec 7.5.1).</summary>
    public readonly struct NodeBanking : ISimEvent
    {
        public string NodeId { get; }
        public long Tick { get; }
        public double Amount { get; }

        public NodeBanking(string nodeId, double amount, long tick)
        {
            NodeId = nodeId;
            Amount = amount;
            Tick = tick;
        }
    }
}
