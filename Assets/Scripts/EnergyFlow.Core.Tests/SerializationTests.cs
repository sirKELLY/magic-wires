using System.Collections.Generic;
using System.IO;
using EnergyFlow.Core;
using EnergyFlow.Core.Nodes;
using NUnit.Framework;

namespace EnergyFlow.Core.Tests
{
    public class SerializationTests
    {
        private static readonly NodeTypeRegistry Registry = NodeTypeRegistry.CreateDefault();

        private static List<string> RunWithLog(Simulation sim, long ticks, long tickOffset = 0)
        {
            var log = new EventLog(sim.Events);
            for (long t = 0; t < ticks; t++)
            {
                TestGraphs.ApplySchedule(sim, t + tickOffset);
                sim.Step();
            }
            return log.Entries;
        }

        [Test]
        public void DefinitionRoundTrip_ProducesFunctionallyIdenticalGraph()
        {
            // Spec 7.6.2: serialize → deserialize → same deterministic behavior.
            var original = TestGraphs.BuildRepresentative();
            string json = GraphSerializer.Serialize(original);
            var restored = GraphSerializer.Deserialize(json, Registry);

            var logA = RunWithLog(original, 1000);
            var logB = RunWithLog(restored, 1000);
            CollectionAssert.AreEqual(logA, logB);
            Assert.AreEqual(original.TotalConsumed, restored.TotalConsumed);
        }

        [Test]
        public void SerializedJson_IsStable()
        {
            var sim = TestGraphs.BuildRepresentative();
            Assert.AreEqual(GraphSerializer.Serialize(sim), GraphSerializer.Serialize(sim));
        }

        [Test]
        public void DefinitionOnlyFile_LoadsAtTickZeroWithEmptyBuffers()
        {
            // Spec 7.6.3.
            var sim = GraphSerializer.Deserialize(File.ReadAllText(TestGraphs.FixturePath), Registry);
            Assert.AreEqual(0, sim.CurrentTick);
            Assert.AreEqual(0.0, sim.TotalHeld);
            Assert.AreEqual(0.0, sim.TotalProduced);
        }

        [Test]
        public void FixtureFile_MatchesProgrammaticGraph()
        {
            var fromFile = GraphSerializer.Deserialize(File.ReadAllText(TestGraphs.FixturePath), Registry);
            var programmatic = TestGraphs.BuildRepresentative();
            CollectionAssert.AreEqual(RunWithLog(programmatic, 1000), RunWithLog(fromFile, 1000));
        }

        [Test]
        public void StateRoundTrip_ResumesMidSessionExactly()
        {
            // Spec 7.6.3: a mid-session save must resume identically.
            var live = TestGraphs.BuildRepresentative();
            RunWithLog(live, 137);
            string saved = GraphSerializer.Serialize(live, includeState: true);
            var resumed = GraphSerializer.Deserialize(saved, Registry);

            Assert.AreEqual(live.CurrentTick, resumed.CurrentTick);
            Assert.AreEqual(live.TotalProduced, resumed.TotalProduced);
            Assert.AreEqual(live.TotalConsumed, resumed.TotalConsumed);

            var logLive = RunWithLog(live, 500, tickOffset: 137);
            var logResumed = RunWithLog(resumed, 500, tickOffset: 137);
            CollectionAssert.AreEqual(logLive, logResumed);
            Assert.AreEqual(live.TotalHeld, resumed.TotalHeld);
        }

        [Test]
        public void StateRoundTrip_PreservesSwitchRuntimeState()
        {
            var live = TestGraphs.BuildRepresentative();
            live.SetSwitchOpen("sw1", false);
            live.Step();
            string saved = GraphSerializer.Serialize(live, includeState: true);
            var resumed = GraphSerializer.Deserialize(saved, Registry);
            Assert.IsFalse(((SwitchNode)resumed.Graph.GetNode("sw1")).Open);
        }

        [Test]
        public void ReservedFields_RoundTripIncludingInfiniteDefaults()
        {
            // Spec 4: capacity and throughputCap serialize from day one; null = infinite.
            var source = new SourceNode("s") { ProductionRate = 1, Capacity = 42 };
            var sink = new SinkNode("k") { Cost = 1 };
            var sim = new Simulation(new Graph(
                new Node[] { source, sink },
                new[] { new Edge("s", 0, "k", 0, throughputCap: 7) }));

            string json = GraphSerializer.Serialize(sim);
            StringAssert.Contains("\"capacity\": 42", json);
            StringAssert.Contains("\"capacity\": null", json); // sink's infinite default
            StringAssert.Contains("\"throughputCap\": 7", json);

            var restored = GraphSerializer.Deserialize(json, Registry);
            Assert.AreEqual(42.0, restored.Graph.GetNode("s").Capacity);
            Assert.IsTrue(double.IsPositiveInfinity(restored.Graph.GetNode("k").Capacity));
            Assert.AreEqual(7.0, restored.Graph.Edges[0].ThroughputCap);
        }

        [Test]
        public void MissingFormatVersion_Throws()
        {
            Assert.Throws<System.FormatException>(() =>
                GraphSerializer.Deserialize("{ \"nodes\": [], \"edges\": [] }", Registry));
        }
    }
}
