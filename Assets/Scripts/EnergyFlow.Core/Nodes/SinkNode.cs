using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Sink (spec 4.7): always accepting; accumulates flow and, each tick,
    /// while held >= cost emits one discrete fire event per cost crossed and
    /// banks the remainder. What a fire event means is host-supplied via
    /// <see cref="ISink"/> or the event bus — the core knows nothing about it.
    /// </summary>
    public sealed class SinkNode : Node
    {
        public const string TypeName = "sink";

        public SinkNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 1;
        public override int MaxInputs => 1;
        public override int MinOutputs => 0;
        public override int MaxOutputs => 0;

        public double Cost { get; set; }

        /// <summary>Optional host-supplied fire receiver (spec 7.2). Observation only.</summary>
        public ISink Receiver { get; set; }

        public override void ValidateConfig()
        {
            if (Cost <= 0)
                throw new GraphValidationException($"Sink '{Id}' cost must be > 0.");
        }

        public override void Tick(double input, INodeContext ctx)
        {
            Held += input;
            while (Held >= Cost)
            {
                Held -= Cost;
                ctx.Emit(new SinkFired(Id, Cost, ctx.Tick));
                Receiver?.OnFire(Cost, ctx.Tick);
            }
        }

        public override void WriteConfig(JObject config)
        {
            config["cost"] = Cost;
            base.WriteConfig(config);
        }

        public override void ReadConfig(JObject config)
        {
            Cost = config["cost"]?.Value<double>() ?? 0.0;
            base.ReadConfig(config);
        }
    }
}
