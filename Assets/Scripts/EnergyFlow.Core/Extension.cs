namespace EnergyFlow.Core
{
    /// <summary>
    /// Host extension point (spec 7.2): supplies a source's production per tick
    /// (themed: spring, reactor, pump…). Implementations must be deterministic
    /// functions of the tick to preserve spec 6.4. When a SourceNode has no
    /// provider it produces its configured constant productionRate.
    /// </summary>
    public interface ISource
    {
        double GetProduction(long tick);
    }

    /// <summary>
    /// Host extension point (spec 7.2): receives discrete fire events (themed:
    /// weapon, building load…). What a fire event does is entirely host-defined;
    /// the core only reports the amount consumed. Observation only — must not
    /// mutate sim state.
    /// </summary>
    public interface ISink
    {
        void OnFire(double amount, long tick);
    }
}
