using System.Collections.Generic;
using EnergyFlow.Core;
using EnergyFlow.Core.Nodes;
using NUnit.Framework;

namespace EnergyFlow.Core.Tests
{
    public class NodeBehaviorTests
    {
        private const double Eps = 1e-9;

        private static Simulation Sim(IEnumerable<Node> nodes, IEnumerable<Edge> edges, EventBus bus = null) =>
            new Simulation(new Graph(nodes, edges), bus);

        [Test]
        public void Sink_FiresOncePerCostCrossed_AndBanksRemainder()
        {
            // Spec 4.7: held 12, cost 5 → two events, holds 2.
            var sink = new SinkNode("k") { Cost = 5 };
            var bus = new EventBus();
            int fires = 0;
            bus.Subscribe<SinkFired>(e => fires++);
            var sim = Sim(
                new Node[] { new SourceNode("s") { ProductionRate = 12 }, sink },
                new[] { new Edge("s", 0, "k", 0) }, bus);

            sim.Step();

            Assert.AreEqual(2, fires);
            Assert.AreEqual(2.0, sink.Held, Eps);
            Assert.AreEqual(10.0, sim.TotalConsumed, Eps);
        }

        [Test]
        public void Merger_SumsInputsAdditively()
        {
            // Spec 4.3: inputs 3 and 2 → output 5.
            var sink = new SinkNode("k") { Cost = 1000 };
            var sim = Sim(
                new Node[]
                {
                    new SourceNode("a") { ProductionRate = 3 },
                    new SourceNode("b") { ProductionRate = 2 },
                    new MergerNode("m"),
                    sink,
                },
                new[]
                {
                    new Edge("a", 0, "m", 0),
                    new Edge("b", 0, "m", 1),
                    new Edge("m", 0, "k", 0),
                });

            sim.Step();
            Assert.AreEqual(5.0, sink.Held, Eps);
        }

        [Test]
        public void Splitter_DistributesByWeights()
        {
            // Spec 4.4: weights 5,1,1 all accepting → 5/7, 1/7, 1/7.
            var k1 = new SinkNode("k1") { Cost = 1000 };
            var k2 = new SinkNode("k2") { Cost = 1000 };
            var k3 = new SinkNode("k3") { Cost = 1000 };
            var sim = Sim(
                new Node[]
                {
                    new SourceNode("s") { ProductionRate = 7 },
                    new SplitterNode("sp") { Weights = new List<double> { 5, 1, 1 } },
                    k1, k2, k3,
                },
                new[]
                {
                    new Edge("s", 0, "sp", 0),
                    new Edge("sp", 0, "k1", 0),
                    new Edge("sp", 1, "k2", 0),
                    new Edge("sp", 2, "k3", 0),
                });

            sim.Step();
            Assert.AreEqual(5.0, k1.Held, Eps);
            Assert.AreEqual(1.0, k2.Held, Eps);
            Assert.AreEqual(1.0, k3.Held, Eps);
        }

        [Test]
        public void Splitter_RenormalizesAroundClosedPath_AndRecovers()
        {
            // Spec 4.4: weights 5,1,1 with third path closed → 5/6, 1/6, 0; reopened → 5/7 again.
            var k1 = new SinkNode("k1") { Cost = 1000 };
            var k2 = new SinkNode("k2") { Cost = 1000 };
            var k3 = new SinkNode("k3") { Cost = 1000 };
            var sim = Sim(
                new Node[]
                {
                    new SourceNode("s") { ProductionRate = 7 },
                    new SplitterNode("sp") { Weights = new List<double> { 5, 1, 1 } },
                    new SwitchNode("sw") { Open = true },
                    k1, k2, k3,
                },
                new[]
                {
                    new Edge("s", 0, "sp", 0),
                    new Edge("sp", 0, "k1", 0),
                    new Edge("sp", 1, "k2", 0),
                    new Edge("sp", 2, "sw", 0),
                    new Edge("sw", 0, "k3", 0),
                });

            sim.SetSwitchOpen("sw", false);
            sim.Step();
            Assert.AreEqual(7.0 * 5 / 6, k1.Held, Eps);
            Assert.AreEqual(7.0 * 1 / 6, k2.Held, Eps);
            Assert.AreEqual(0.0, k3.Held, Eps);

            sim.SetSwitchOpen("sw", true);
            sim.Step();
            Assert.AreEqual(7.0 * 5 / 6 + 7.0 * 5 / 7, k1.Held, Eps);
            Assert.AreEqual(7.0 * 1 / 6 + 7.0 * 1 / 7, k2.Held, Eps);
            Assert.AreEqual(7.0 * 1 / 7, k3.Held, Eps);
        }

        [Test]
        public void ClosedSwitch_PropagatesUpstream_SourceBanks_ThenReleases()
        {
            // Spec 4.1/3.3: closed path reaches the source via the accept pass;
            // the source banks and pushes the backlog when the path reopens.
            var source = new SourceNode("s") { ProductionRate = 10 };
            var sink = new SinkNode("k") { Cost = 1000 };
            var bus = new EventBus();
            int bankEvents = 0;
            bus.Subscribe<NodeBanking>(e => bankEvents++);
            var sim = Sim(
                new Node[] { source, new SwitchNode("sw") { Open = true }, sink },
                new[] { new Edge("s", 0, "sw", 0), new Edge("sw", 0, "k", 0) }, bus);

            sim.SetSwitchOpen("sw", false);
            sim.Step(3);
            Assert.AreEqual(30.0, source.Held, Eps);
            Assert.AreEqual(0.0, sink.Held, Eps);
            Assert.AreEqual(3, bankEvents);

            sim.SetSwitchOpen("sw", true);
            sim.Step();
            Assert.AreEqual(0.0, source.Held, Eps);
            Assert.AreEqual(40.0, sink.Held, Eps); // 30 banked + 10 produced
        }

        [Test]
        public void Gate_OverfillDischargesExactlyThreshold()
        {
            // Spec 4.5: holds 31 → discharge 30 → holds 1.
            var gate = new GateNode("g") { Threshold = 30 };
            var sink = new SinkNode("k") { Cost = 1000 };
            var bus = new EventBus();
            var discharges = new List<double>();
            bus.Subscribe<GateDischarged>(e => discharges.Add(e.Amount));
            var sim = Sim(
                new Node[] { new SourceNode("s") { ProductionRate = 31 }, gate, sink },
                new[] { new Edge("s", 0, "g", 0), new Edge("g", 0, "k", 0) }, bus);

            sim.Step();

            CollectionAssert.AreEqual(new[] { 30.0 }, discharges);
            Assert.AreEqual(1.0, gate.Held, Eps);
            Assert.AreEqual(30.0, sink.Held, Eps);
        }

        [Test]
        public void Gate_BelowThreshold_HoldsAndDoesNotDischarge()
        {
            var gate = new GateNode("g") { Threshold = 30 };
            var sink = new SinkNode("k") { Cost = 1000 };
            var sim = Sim(
                new Node[] { new SourceNode("s") { ProductionRate = 10 }, gate, sink },
                new[] { new Edge("s", 0, "g", 0), new Edge("g", 0, "k", 0) });

            sim.Step(2);
            Assert.AreEqual(20.0, gate.Held, Eps);
            Assert.AreEqual(0.0, sink.Held, Eps);

            sim.Step();
            Assert.AreEqual(0.0, gate.Held, Eps);
            Assert.AreEqual(30.0, sink.Held, Eps);
        }

        [Test]
        public void Gate_WithClosedOutput_HoldsEvenAboveThreshold()
        {
            // Spec 4.5: if its output is not accepting, it holds and does not discharge.
            // State section loads a gate already above threshold (spec 7.6.3).
            const string json = @"{
              ""formatVersion"": 1,
              ""nodes"": [
                { ""id"": ""s"", ""type"": ""source"", ""config"": { ""productionRate"": 0.0, ""capacity"": null } },
                { ""id"": ""g"", ""type"": ""gate"", ""config"": { ""threshold"": 30.0, ""capacity"": null } },
                { ""id"": ""sw"", ""type"": ""switch"", ""config"": { ""open"": false, ""capacity"": null } },
                { ""id"": ""k"", ""type"": ""sink"", ""config"": { ""cost"": 5.0, ""capacity"": null } }
              ],
              ""edges"": [
                { ""from"": ""s"", ""fromPort"": 0, ""to"": ""g"", ""toPort"": 0, ""throughputCap"": null },
                { ""from"": ""g"", ""fromPort"": 0, ""to"": ""sw"", ""toPort"": 0, ""throughputCap"": null },
                { ""from"": ""sw"", ""fromPort"": 0, ""to"": ""k"", ""toPort"": 0, ""throughputCap"": null }
              ],
              ""state"": { ""tick"": 0, ""nodes"": { ""g"": { ""held"": 31.0 } } }
            }";
            var sim = GraphSerializer.Deserialize(json, NodeTypeRegistry.CreateDefault());
            var gate = (GateNode)sim.Graph.GetNode("g");

            sim.Step(5);
            Assert.AreEqual(31.0, gate.Held, Eps);

            sim.SetSwitchOpen("sw", true);
            sim.Step();
            Assert.AreEqual(1.0, gate.Held, Eps);
        }

        [Test]
        public void SwitchToggle_TakesEffectOnNextTickBoundary()
        {
            // Spec 6.3: toggles apply on a tick boundary, not mid-tick.
            var sw = new SwitchNode("sw") { Open = true };
            var bus = new EventBus();
            long? effectTick = null;
            bus.Subscribe<SwitchToggled>(e => effectTick = e.Tick);
            var sim = Sim(
                new Node[] { new SourceNode("s") { ProductionRate = 1 }, sw, new SinkNode("k") { Cost = 1000 } },
                new[] { new Edge("s", 0, "sw", 0), new Edge("sw", 0, "k", 0) }, bus);

            sim.Step(); // tick 0 runs open
            sim.SetSwitchOpen("sw", false);
            Assert.IsTrue(sw.Open, "toggle must not apply before the next Step");

            sim.Step(); // applied at the start of tick 1
            Assert.IsFalse(sw.Open);
            Assert.AreEqual(1, effectTick);
        }

        [Test]
        public void SetSwitchOpen_OnNonSwitch_Throws()
        {
            var sim = Sim(
                new Node[] { new SourceNode("s") { ProductionRate = 1 }, new SinkNode("k") { Cost = 1 } },
                new[] { new Edge("s", 0, "k", 0) });
            Assert.Throws<System.ArgumentException>(() => sim.SetSwitchOpen("k", false));
        }

        [Test]
        public void HostProviders_AreConsulted()
        {
            var source = new SourceNode("s"); // rate 0; provider supplies instead
            source.Provider = new RampSource();
            var fired = new List<double>();
            var sink = new SinkNode("k") { Cost = 3, Receiver = new RecordingSink(fired) };
            var sim = Sim(new Node[] { source, sink }, new[] { new Edge("s", 0, "k", 0) });

            sim.Step(3); // produces 1, 2, 3 → held crosses 3 twice
            Assert.AreEqual(6.0, sim.TotalProduced, Eps);
            CollectionAssert.AreEqual(new[] { 3.0, 3.0 }, fired);
        }

        private sealed class RampSource : ISource
        {
            public double GetProduction(long tick) => tick + 1;
        }

        private sealed class RecordingSink : ISink
        {
            private readonly List<double> _fired;
            public RecordingSink(List<double> fired) { _fired = fired; }
            public void OnFire(double amount, long tick) => _fired.Add(amount);
        }
    }
}
