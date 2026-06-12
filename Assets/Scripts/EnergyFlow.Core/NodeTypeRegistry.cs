using System;
using System.Collections.Generic;
using EnergyFlow.Core.Nodes;

namespace EnergyFlow.Core
{
    /// <summary>
    /// Node-type registry (spec 7.7). Types are registered against the Node
    /// contract by stable string ID; the engine and serializer operate on the
    /// contract only. The seven core types are the standard library; future
    /// types are plugins registered here, never engine edits.
    /// (The Conduit is the edge, spec 4.2 — it has no node class.)
    /// </summary>
    public sealed class NodeTypeRegistry
    {
        private readonly Dictionary<string, Func<string, Node>> _factories =
            new Dictionary<string, Func<string, Node>>(StringComparer.Ordinal);

        public void Register(string typeId, Func<string, Node> factory)
        {
            if (string.IsNullOrEmpty(typeId)) throw new ArgumentException("Type id must be non-empty.", nameof(typeId));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_factories.ContainsKey(typeId))
                throw new ArgumentException($"Node type '{typeId}' is already registered.", nameof(typeId));
            _factories[typeId] = factory;
        }

        public bool IsRegistered(string typeId) => _factories.ContainsKey(typeId);

        public Node Create(string typeId, string nodeId)
        {
            if (!_factories.TryGetValue(typeId, out var factory))
                throw new KeyNotFoundException($"Node type '{typeId}' is not registered.");
            return factory(nodeId);
        }

        /// <summary>A registry pre-loaded with the standard library (spec 7.7.2).</summary>
        public static NodeTypeRegistry CreateDefault()
        {
            var registry = new NodeTypeRegistry();
            registry.Register(SourceNode.TypeName, id => new SourceNode(id));
            registry.Register(MergerNode.TypeName, id => new MergerNode(id));
            registry.Register(SplitterNode.TypeName, id => new SplitterNode(id));
            registry.Register(GateNode.TypeName, id => new GateNode(id));
            registry.Register(SwitchNode.TypeName, id => new SwitchNode(id));
            registry.Register(SinkNode.TypeName, id => new SinkNode(id));
            return registry;
        }
    }
}
