using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core
{
    /// <summary>
    /// JSON graph serialization (spec 7.6): a circuit is a file. Captures node
    /// IDs, types, per-node config, all edges, and (optionally) runtime state
    /// in a separate, clearly-marked section. Definition-only files load at
    /// tick 0 with empty buffers. Human-readable: indented, stable key order.
    /// </summary>
    public static class GraphSerializer
    {
        public const int CurrentFormatVersion = 1;

        public static string Serialize(Simulation sim, bool includeState = false)
        {
            var root = new JObject { ["formatVersion"] = CurrentFormatVersion };

            var nodes = new JArray();
            // Topological order is deterministic, so files are byte-stable.
            foreach (var node in sim.Graph.TopologicalOrder)
            {
                var config = new JObject();
                node.WriteConfig(config);
                nodes.Add(new JObject
                {
                    ["id"] = node.Id,
                    ["type"] = node.TypeId,
                    ["config"] = config,
                });
            }
            root["nodes"] = nodes;

            var edges = new JArray();
            foreach (var edge in sim.Graph.Edges)
            {
                edges.Add(new JObject
                {
                    ["from"] = edge.FromNodeId,
                    ["fromPort"] = edge.FromPort,
                    ["to"] = edge.ToNodeId,
                    ["toPort"] = edge.ToPort,
                    ["throughputCap"] = JsonNumbers.Write(edge.ThroughputCap),
                });
            }
            root["edges"] = edges;

            if (includeState)
            {
                var nodeStates = new JObject();
                foreach (var node in sim.Graph.TopologicalOrder)
                {
                    var state = new JObject();
                    node.WriteState(state);
                    nodeStates[node.Id] = state;
                }
                root["state"] = new JObject
                {
                    ["tick"] = sim.CurrentTick,
                    ["totalProduced"] = sim.TotalProduced,
                    ["totalConsumed"] = sim.TotalConsumed,
                    ["nodes"] = nodeStates,
                };
            }

            return root.ToString(Formatting.Indented);
        }

        public static Simulation Deserialize(string json, NodeTypeRegistry registry, EventBus events = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var root = JObject.Parse(json);

            var version = root["formatVersion"]?.Value<int>();
            if (version == null)
                throw new FormatException("Graph file is missing 'formatVersion'.");

            var nodes = new List<Node>();
            foreach (var nodeToken in (JArray)root["nodes"])
            {
                var obj = (JObject)nodeToken;
                var node = registry.Create(obj["type"].Value<string>(), obj["id"].Value<string>());
                node.ReadConfig((JObject)obj["config"] ?? new JObject());
                nodes.Add(node);
            }

            var edges = new List<Edge>();
            foreach (var edgeToken in (JArray)(root["edges"] ?? new JArray()))
            {
                var obj = (JObject)edgeToken;
                edges.Add(new Edge(
                    obj["from"].Value<string>(),
                    obj["fromPort"].Value<int>(),
                    obj["to"].Value<string>(),
                    obj["toPort"].Value<int>(),
                    JsonNumbers.Read(obj["throughputCap"])));
            }

            var graph = new Graph(nodes, edges);

            var state = (JObject)root["state"];
            return state == null
                ? new Simulation(graph, events)
                : RestoreState(graph, events, state);
        }

        private static Simulation RestoreState(Graph graph, EventBus events, JObject state)
        {
            long tick = state["tick"]?.Value<long>() ?? 0;
            var sim = new Simulation(graph, events, tick)
            {
                TotalProduced = state["totalProduced"]?.Value<double>() ?? 0.0,
                TotalConsumed = state["totalConsumed"]?.Value<double>() ?? 0.0,
            };
            var nodeStates = (JObject)state["nodes"];
            if (nodeStates != null)
            {
                foreach (var property in nodeStates.Properties())
                {
                    if (graph.TryGetNode(property.Name, out var node))
                        node.ReadState((JObject)property.Value);
                }
            }
            return sim;
        }
    }
}
