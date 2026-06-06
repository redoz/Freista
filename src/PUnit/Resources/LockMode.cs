namespace PUnit;

/// <summary>
/// The reader/writer mode a <see cref="LifecycleVerb"/> implies: <c>Read</c>/<c>Load</c> are
/// <see cref="Shared"/>; <c>Create</c>/<c>Edit</c>/<c>Delete</c> are <see cref="Exclusive"/>.
/// </summary>
public enum LockMode
{
    /// <summary>Coexists with other shared holders of the same identity.</summary>
    Shared,

    /// <summary>Excludes every other holder of the same identity.</summary>
    Exclusive,
}
