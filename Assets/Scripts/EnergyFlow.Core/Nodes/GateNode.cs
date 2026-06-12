using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Gate / threshold bucket (spec 4.5): fills continuously, overfill allowed.
    /// When held >= threshold and the output accepts, it discharges exactly
    /// threshold units as one instant packet in a single tick (31 → discharge
    /// 30 → holds 1). If the output is not accepting it holds and does not
    /// discharge. Holding is the gate's defined behavior, not banking, so no
    /// NodeBanking event is emitted.
    /// </summary>
    public sealed class GateNode : Node
    {
        public const string TypeName = "gate";

        public GateNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 1;
        public override int MaxInputs => 1;
        public override int MinOutputs => 1;
        public override int MaxOutputs => 1;

        public double Threshold { get; set; }

        public override void ValidateConfig()
        {
            if (Threshold <= 0)
                throw new GraphValidationException($"Gate '{Id}' threshold must be > 0.");
        }

        public override void Tick(double input, INodeContext ctx)
        {
            Held += input;
            if (Held >= Threshold && ctx.IsOutputAccepting(0))
            {
                Held -= Threshold;
                ctx.Push(0, Threshold);
                ctx.Emit(new GateDischarged(Id, Threshold, ctx.Tick));
            }
        }

        public override void WriteConfig(JObject config)
        {
            config["threshold"] = Threshold;
            base.WriteConfig(config);
        }

        public override void ReadConfig(JObject config)
        {
            Threshold = config["threshold"]?.Value<double>() ?? 0.0;
            base.ReadConfig(config);
        }
    }
}
