using System;
using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core
{
    /// <summary>
    /// Context handed to a node during Pass 2 (spec 3.3). A node sees only its
    /// immediate ports — never the wider graph (spec 7.3).
    /// </summary>
    public interface INodeContext
    {
        long Tick { get; }
        int OutputCount { get; }

        /// <summary>Whether the node connected to the given output port is accepting (Pass 1 result).</summary>
        bool IsOutputAccepting(int outputPort);

        /// <summary>Push flow across the given output port. The target must be accepting.</summary>
        void Push(int outputPort, double amount);

        /// <summary>Emit a typed event onto the bus (spec 7.5).</summary>
        void Emit(ISimEvent evt);
    }

    /// <summary>
    /// Implemented by nodes whose state the host layer may toggle (the Switch).
    /// </summary>
    public interface IToggleable
    {
        bool Open { get; set; }
    }

    /// <summary>
    /// The node-type contract (spec 7.7.1). A node declares its port arity, its
    /// Pass 1 local accept condition, its Pass 2 flow behavior, and its
    /// configurable fields (via the config/state serialization hooks). The
    /// engine's tick loop, validation, serialization and event bus operate on
    /// this contract only, never on concrete node classes.
    /// </summary>
    public abstract class Node
    {
        protected Node(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Node id must be non-empty.", nameof(id));
            Id = id;
        }

        public string Id { get; }

        /// <summary>Registered string type ID (spec 7.7.3).</summary>
        public abstract string TypeId { get; }

        /// <summary>
        /// Flow currently held/banked in this node. Universal banking storage
        /// (spec 2.4): gate fill, sink accumulator, source buffer and splitter
        /// bank all live here, so conservation is a plain sum over nodes.
        /// </summary>
        public double Held { get; protected internal set; }

        /// <summary>
        /// Reserved field (spec 4): stored and serialized from day one, no
        /// behavior in Phase A. Infinite = uncapped. Phase B activates it as a
        /// local accept condition.
        /// </summary>
        public double Capacity { get; set; } = double.PositiveInfinity;

        public abstract int MinInputs { get; }
        public abstract int MaxInputs { get; }
        public abstract int MinOutputs { get; }
        public abstract int MaxOutputs { get; }

        /// <summary>
        /// Pass 1 local accept condition (spec 3.3). True for every node type
        /// except Switch, whose condition is its open flag. Later phases add
        /// new conditions here only — the passes themselves are frozen.
        /// </summary>
        public virtual bool LocalAccept() => true;

        /// <summary>
        /// Pass 2 flow behavior. <paramref name="input"/> is the sum of flow
        /// delivered on all input edges this tick.
        /// </summary>
        public abstract void Tick(double input, INodeContext ctx);

        /// <summary>Called at graph build time with the node's actual edge counts (spec 5.3).</summary>
        public virtual void ValidateTopology(int inputCount, int outputCount)
        {
            if (inputCount < MinInputs || inputCount > MaxInputs)
                throw new GraphValidationException(
                    $"Node '{Id}' ({TypeId}) has {inputCount} input(s); allowed range is {MinInputs}..{MaxInputs}.");
            if (outputCount < MinOutputs || outputCount > MaxOutputs)
                throw new GraphValidationException(
                    $"Node '{Id}' ({TypeId}) has {outputCount} output(s); allowed range is {MinOutputs}..{MaxOutputs}.");
        }

        /// <summary>Called at graph build time to validate configurable fields.</summary>
        public virtual void ValidateConfig() { }

        /// <summary>Write per-node configuration (spec 7.6.1). Subclasses write their fields, then call base.</summary>
        public virtual void WriteConfig(JObject config)
        {
            config["capacity"] = JsonNumbers.Write(Capacity);
        }

        public virtual void ReadConfig(JObject config)
        {
            Capacity = JsonNumbers.Read(config["capacity"]);
        }

        /// <summary>Write runtime state for the optional state section (spec 7.6.3).</summary>
        public virtual void WriteState(JObject state)
        {
            state["held"] = Held;
        }

        public virtual void ReadState(JObject state)
        {
            Held = state["held"]?.Value<double>() ?? 0.0;
        }
    }

    /// <summary>
    /// JSON convention for the reserved infinite-default fields: null = infinite
    /// (standard JSON has no Infinity literal).
    /// </summary>
    public static class JsonNumbers
    {
        public static JToken Write(double value) =>
            double.IsPositiveInfinity(value) ? JValue.CreateNull() : (JToken)value;

        public static double Read(JToken token) =>
            token == null || token.Type == JTokenType.Null ? double.PositiveInfinity : token.Value<double>();
    }
}
