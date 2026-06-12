using Newtonsoft.Json.Linq;

namespace EnergyFlow.Core.Nodes
{
    /// <summary>
    /// Switch (spec 4.6): a pure boolean. Open passes input straight through;
    /// closed makes it not-accepting (the only non-trivial local accept
    /// condition in Phase A), which propagates backward in Pass 1 and causes
    /// upstream splitters to renormalize. Toggled by the host via
    /// <see cref="Simulation.SetSwitchOpen"/> on tick boundaries.
    /// </summary>
    public sealed class SwitchNode : Node, IToggleable
    {
        public const string TypeName = "switch";

        public SwitchNode(string id) : base(id) { }

        public override string TypeId => TypeName;
        public override int MinInputs => 1;
        public override int MaxInputs => 1;
        public override int MinOutputs => 1;
        public override int MaxOutputs => 1;

        public bool Open { get; set; } = true;

        public override bool LocalAccept() => Open;

        public override void Tick(double input, INodeContext ctx)
        {
            double total = Held + input;
            Held = 0;
            if (total <= 0) return;

            // Universal banking (spec 2.4); unreachable in normal flow because a
            // closed or blocked switch is not accepting and receives nothing.
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
            config["open"] = Open;
            base.WriteConfig(config);
        }

        public override void ReadConfig(JObject config)
        {
            Open = config["open"]?.Value<bool>() ?? true;
            base.ReadConfig(config);
        }

        public override void WriteState(JObject state)
        {
            base.WriteState(state);
            state["open"] = Open;
        }

        public override void ReadState(JObject state)
        {
            base.ReadState(state);
            if (state["open"] != null) Open = state["open"].Value<bool>();
        }
    }
}
