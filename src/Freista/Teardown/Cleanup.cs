namespace Freista;

/// <summary>
/// Whether a registered cleanup is optional or mandatory. This is a KIND, not a policy: leaving
/// database rows behind for inspection is a choice, but leaving a container running is a leak, so a
/// <see cref="Required"/> cleanup runs even under <see cref="Run.Never"/>.
/// </summary>
public enum Cleanup
{
    /// <summary>Runs only when the scenario's <see cref="Run"/> policy allows it.</summary>
    Optional,

    /// <summary>Always runs, whatever the scenario's policy — including after cancellation.</summary>
    Required,
}
