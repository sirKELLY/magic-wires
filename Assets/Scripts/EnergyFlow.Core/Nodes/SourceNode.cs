using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Source (spec 4.1): produces productionRate units per tick and pushes to
    /// its single output. If the output is not accepting it banks the production
    /// in its internal buffer (Held) and pushes it when the path reopens.
    /// </summary>
    public sealed class SourceNode : Node
    {
        public const string TypeName = "source";

        public SourceNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 0;
        public override int MaxInputs => 0;
        public override int MinOutputs => 1;
        public override int MaxOutputs => 1;

        public double ProductionRate { get; set; }

        /// <summary>Optional host-supplied production (spec 7.2). Null = constant ProductionRate.</summary>
        public ISource Provider { get; set; }

        public override void ValidateConfig()
        {
            if (ProductionRate < 0)
                throw new GraphValidationException($"Source '{Id}' productionRate must be >= 0.");
        }

        public override void Tick(double input, INodeContext ctx)
        {
            double produced = Provider?.GetProduction(ctx.Tick) ?? ProductionRate;
            if (produced < 0) produced = 0;
            if (produced > 0) ctx.Emit(new SourceProduced(Id, produced, ctx.Tick));

            double total = Held + produced;
            Held = 0;
            if (total <= 0) return;

            if (ctx.IsOutputAccepting(0))
            {
                ctx.Push(0, total);
            }
            else
            {
                Held = total;
                ctx.Emit(new NodeBanking(Id, Held, ctx.Tick));
            }
        }

        public override void WriteConfig(JObject config)
        {
            config["productionRate"] = ProductionRate;
            base.WriteConfig(config);
        }

        public override void ReadConfig(JObject config)
        {
            ProductionRate = config["productionRate"]?.Value<double>() ?? 0.0;
            base.ReadConfig(config);
        }
    }
}
