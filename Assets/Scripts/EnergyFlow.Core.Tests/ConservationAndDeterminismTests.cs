using System;
using System.Collections.Generic;
using System.IO;
using EnergyFlow.Core;
using NUnit.Framework;

namespace EnergyFlow.Core.Tests
{
    public class ConservationAndDeterminismTests
    {
        [Test]
        public void Conservation_HoldsEveryTick_For10000Ticks()
        {
            // Spec 6.5: produced = consumed + held, every tick, within float
            // epsilon, on a representative graph (5:1:1 splitter, switch
            // toggles, gate discharges). A violation is a build-blocking bug.
            var sim = GraphSerializer.Deserialize(
                File.ReadAllText(TestGraphs.FixturePath), NodeTypeRegistry.CreateDefault());

            for (long t = 0; t < 10_000; t++)
            {
                TestGraphs.ApplySchedule(sim, t);
                sim.Step();

                double produced = sim.TotalProduced;
                double accounted = sim.TotalConsumed + sim.TotalHeld;
                double tolerance = 1e-6 * Math.Max(1.0, produced);
                Assert.AreEqual(produced, accounted, tolerance,
                    $"Conservation violated at tick {t}: produced={produced:R}, " +
                    $"consumed={sim.TotalConsumed:R}, held={sim.TotalHeld:R}");
            }

            // Sanity: the graph actually exercised flow, not a vacuous pass.
            Assert.Greater(sim.TotalProduced, 0.0);
            Assert.Greater(sim.TotalConsumed, 0.0);
        }

        [Test]
        public void IdenticalInputs_ProduceIdenticalRuns()
        {
            // Spec 6.4: same graph + same (tick, toggle) inputs → identical
            // output flow and fire events across runs.
            (List<string> log, double produced, double consumed, double held) Run()
            {
                var bus = new EventBus();
                var log = new EventLog(bus);
                var sim = GraphSerializer.Deserialize(
                    File.ReadAllText(TestGraphs.FixturePath), NodeTypeRegistry.CreateDefault(), bus);
                for (long t = 0; t < 2000; t++)
                {
                    TestGraphs.ApplySchedule(sim, t);
                    sim.Step();
                }
                return (log.Entries, sim.TotalProduced, sim.TotalConsumed, sim.TotalHeld);
            }

            var a = Run();
            var b = Run();
            Assert.AreEqual(a.produced, b.produced);
            Assert.AreEqual(a.consumed, b.consumed);
            Assert.AreEqual(a.held, b.held);
            CollectionAssert.AreEqual(a.log, b.log);
            Assert.Greater(a.log.Count, 0);
        }
    }
}
