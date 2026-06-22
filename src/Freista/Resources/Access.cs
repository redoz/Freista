namespace Freista;

/// <summary>
/// The access a step explicitly requests on a resource — the user-facing choice for
/// <c>LockAsync</c> and <c>[Requires&lt;T&gt;]</c>. Maps onto the same reader/writer rule as
/// <see cref="LockMode"/>.
/// </summary>
public enum Access
{
    /// <summary>Many holders may share the resource concurrently.</summary>
    Shared,

    /// <summary>A single holder excludes all others.</summary>
    Exclusive,
}
