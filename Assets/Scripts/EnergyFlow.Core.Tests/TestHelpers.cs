using System.Collections.Generic;
using System.Globalization;
using EnergyFlow.Core;

namespace EnergyFlow.Core.Tests
{
    /// <summary>
    /// Records every core event as a culture-invariant string. An event log is
    /// itself a determinism test artifact (spec 7.5.2).
    /// </summary>
    public sealed class EventLog
    {
        public List<string> Entries { get; } = new List<string>();

        public EventLog(EventBus bus)
        {
            bus.Subscribe<SourceProduced>(e => Add("produced", e.NodeId, e.Tick, e.Amount));
            bus.Subscribe<FlowDelivered>(e => Add("delivered", e.NodeId, e.Tick, e.Amount));
            bus.Subscribe<GateDischarged>(e => Add("discharged", e.NodeId, e.Tick, e.Amount));
            bus.Subscribe<SinkFired>(e => Add("fired", e.NodeId, e.Tick, e.Amount));
            bus.Subscribe<NodeBanking>(e => Add("banked", e.NodeId, e.Tick, e.Amount));
            bus.Subscribe<SwitchToggled>(e => Entries.Add(
                string.Format(CultureInfo.InvariantCulture, "toggled {0} t={1} open={2}", e.NodeId, e.Tick, e.Open)));
        }

        private void Add(string kind, string nodeId, long tick, double amount) =>
            Entries.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} t={2} a={3:R}", kind, nodeId, tick, amount));
    }

    public static class TestGraphs
    {
        public const string FixturePath = "Assets/Scripts/EnergyFlow.Core.Tests/Fixtures/representative-graph.json";

        /// <summary>
        /// The representative graph from spec 6.5: a 5:1:1 splitter (non-terminating
        /// ratios), two switches, two gates, a merger and two sinks.
        /// Mirrors the JSON fixture exactly.
        /// </summary>
        public static Simulation BuildRepresentative(EventBus bus = null)
        {
            var nodes = new List<Node>
            {
                new Nodes.SourceNode("src") { ProductionRate = 10 },
                new Nodes.SplitterNode("split") { Weights = new List<double> { 5, 1, 1 } },
                new Nodes.SwitchNode("sw1") { Open = true },
                new Nodes.GateNode("gate1") { Threshold = 30 },
                new Nodes.SinkNode("sink1") { Cost = 5 },
                new Nodes.GateNode("gate2") { Threshold = 7 },
                new Nodes.SwitchNode("sw2") { Open = true },
                new Nodes.MergerNode("merge"),
                new Nodes.SinkNode("sink2") { Cost = 3 },
            };
            var edges = new List<Edge>
            {
                new Edge("src", 0, "split", 0),
                new Edge("split", 0, "sw1", 0),
                new Edge("split", 1, "gate2", 0),
                new Edge("split", 2, "sw2", 0),
                new Edge("sw1", 0, "gate1", 0),
                new Edge("gate1", 0, "sink1", 0),
                new Edge("gate2", 0, "merge", 0),
                new Edge("sw2", 0, "merge", 1),
                new Edge("merge", 0, "sink2", 0),
            };
            return new Simulation(new Graph(nodes, edges), bus);
        }

        /// <summary>Deterministic switch-toggle schedule used by the conservation and determinism tests.</summary>
        public static void ApplySchedule(Simulation sim, long tick)
        {
            if (tick > 0 && tick % 50 == 0) sim.SetSwitchOpen("sw2", tick % 100 != 0);
            if (tick > 0 && tick % 175 == 0) sim.SetSwitchOpen("sw1", tick % 350 != 0);
        }
    }
}
