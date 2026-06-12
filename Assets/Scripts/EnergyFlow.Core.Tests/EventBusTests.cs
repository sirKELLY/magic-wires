using System.Collections.Generic;
using System.Linq;
using EnergyFlow.Core;
using NUnit.Framework;

namespace EnergyFlow.Core.Tests
{
    public class EventBusTests
    {
        [Test]
        public void SubscribersAreInvokedInSubscriptionOrder()
        {
            var bus = new EventBus();
            var calls = new List<string>();
            bus.Subscribe<SinkFired>(e => calls.Add("first"));
            bus.Subscribe<SinkFired>(e => calls.Add("second"));

            bus.Publish(new SinkFired("k", 5, 0));
            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new EventBus();
            int calls = 0;
            System.Action<SinkFired> handler = e => calls++;
            bus.Subscribe(handler);
            bus.Publish(new SinkFired("k", 5, 0));
            bus.Unsubscribe(handler);
            bus.Publish(new SinkFired("k", 5, 1));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Subscribing_HasZeroEffectOnSimulationBehavior()
        {
            // Spec 7.5.3: observation only. Run the representative graph with a
            // full set of subscribers and with none; all sim state must match.
            var observedBus = new EventBus();
            var unused = new EventLog(observedBus);
            var observed = TestGraphs.BuildRepresentative(observedBus);
            var silent = TestGraphs.BuildRepresentative();

            for (long t = 0; t < 500; t++)
            {
                TestGraphs.ApplySchedule(observed, t);
                TestGraphs.ApplySchedule(silent, t);
                observed.Step();
                silent.Step();
            }

            Assert.AreEqual(silent.TotalProduced, observed.TotalProduced);
            Assert.AreEqual(silent.TotalConsumed, observed.TotalConsumed);
            foreach (var node in silent.Graph.Nodes)
                Assert.AreEqual(node.Held, observed.Graph.GetNode(node.Id).Held,
                    $"Held mismatch on '{node.Id}'");
        }

        [Test]
        public void EventOrder_IsDeterministicAcrossRuns()
        {
            // Spec 7.5.2: the event log is a determinism artifact.
            List<string> Run()
            {
                var bus = new EventBus();
                var log = new EventLog(bus);
                var sim = TestGraphs.BuildRepresentative(bus);
                for (long t = 0; t < 500; t++)
                {
                    TestGraphs.ApplySchedule(sim, t);
                    sim.Step();
                }
                return log.Entries;
            }

            CollectionAssert.AreEqual(Run(), Run());
        }
    }
}
