namespace Freista.Model;

/// <summary>Lifecycle outcome of a single scenario step.</summary>
public enum StepStatus
{
    /// <summary>Not yet started.</summary>
    Pending,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Completed successfully.</summary>
    Passed,

    /// <summary>The operation threw (or timed out).</summary>
    Failed,

    /// <summary>Not run because a dependency failed, was skipped, or the scenario was canceled.</summary>
    Skipped,
}
