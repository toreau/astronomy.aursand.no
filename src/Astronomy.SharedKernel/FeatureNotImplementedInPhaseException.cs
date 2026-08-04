namespace Astronomy.SharedKernel;

public sealed class FeatureNotImplementedInPhaseException(string feature, string phase)
    : NotSupportedException($"{feature} is not implemented in this phase (planned for {phase}).")
{
    public string Feature { get; } = feature;
    public string Phase { get; } = phase;
}
