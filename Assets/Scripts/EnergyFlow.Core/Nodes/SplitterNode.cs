using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Splitter (spec 4.4): one input, multiple weighted outputs. Each tick the
    /// weights of accepting outputs are summed and the input divided in
    /// proportion. No stored percentages, no history — redistribution around
    /// closed paths is emergent from renormalizing every tick. If no outputs
    /// accept, it banks (and Pass 1 already reports it not-accepting).
    /// </summary>
    public sealed class SplitterNode : Node
    {
        public const string TypeName = "splitter";

        public SplitterNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 1;
        public override int MaxInputs => 1;
        public override int MinOutputs => 1;
        public override int MaxOutputs => int.MaxValue;

        /// <summary>Fixed per-output-port weights (spec 8). Never rewritten by the engine.</summary>
        public List<double> Weights { get; set; } = new List<double>();

        public override void ValidateTopology(int inputCount, int outputCount)
        {
            base.ValidateTopology(inputCount, outputCount);
            if (Weights.Count != outputCount)
                throw new GraphValidationException(
                    $"Splitter '{Id}' has {Weights.Count} weight(s) but {outputCount} output edge(s); they must match.");
        }

        public override void ValidateConfig()
        {
            if (Weights.Count == 0 || Weights.Any(w => w <= 0))
                throw new GraphValidationException($"Splitter '{Id}' weights must be a non-empty list of positive numbers.");
        }

        public override void Tick(double input, INodeContext ctx)
        {
            double total = Held + input;
            Held = 0;
            if (total <= 0) return;

            double weightSum = 0;
            int lastAccepting = -1;
            for (int i = 0; i < Weights.Count; i++)
            {
                if (ctx.IsOutputAccepting(i))
                {
                    weightSum += Weights[i];
                    lastAccepting = i;
                }
            }

            if (lastAccepting < 0)
            {
                Held = total;
                ctx.Emit(new NodeBanking(Id, Held, ctx.Tick));
                return;
            }

            // The last accepting output receives the exact remainder so the
            // shares sum to the input bit-for-bit (conservation, spec 6.5).
            double pushed = 0;
            for (int i = 0; i < Weights.Count; i++)
            {
                if (!ctx.IsOutputAccepting(i)) continue;
                double share = i == lastAccepting ? total - pushed : total * (Weights[i] / weightSum);
                pushed += share;
                ctx.Push(i, share);
            }
        }

        public override void WriteConfig(JObject config)
        {
            config["weights"] = new JArray(Weights);
            base.WriteConfig(config);
        }

        public override void ReadConfig(JObject config)
        {
            Weights = config["weights"]?.Values<double>().ToList() ?? new List<double>();
            base.ReadConfig(config);
        }
    }
}
