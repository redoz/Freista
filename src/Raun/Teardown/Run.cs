namespace Raun;

/// <summary>When a scenario's <see cref="Cleanup.Optional"/> teardowns run.</summary>
public enum Run
{
    /// <summary>Run them whether the scenario passed or failed. The default.</summary>
    Always,

    /// <summary>Run them only when every step passed, so a failed scenario leaves its state intact
    /// for inspection.</summary>
    OnSuccess,

    /// <summary>Never run them. <see cref="Cleanup.Required"/> registrations still run.</summary>
    Never,
}
