namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Merger (spec 4.3): the only node permitted multiple input edges. Sums all
    /// incoming flow additively onto its single output. No reconciliation, no loss.
    /// </summary>
    public sealed class MergerNode : Node
    {
        public const string TypeName = "merger";

        public MergerNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 1;
        public override int MaxInputs => int.MaxValue;
        public override int MinOutputs => 1;
        public override int MaxOutputs => 1;

        public override void Tick(double input, INodeContext ctx)
        {
            double total = Held + input;
            Held = 0;
            if (total <= 0) return;

            // Universal banking (spec 2.4). Unreachable in normal flow — a
            // merger whose output chain is closed is itself not accepting and
            // receives nothing — but the rule is universal, not special-cased.
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
    }
}
