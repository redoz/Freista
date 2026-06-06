namespace PUnit;

/// <summary>
/// Type-safe resource identity via CRTP: the domain record IS the identity, projecting its key at
/// the TYPE level (static-abstract <see cref="KeyFor"/>) so generic lock/trace code needs no
/// instance and no reflection. Implement by hand, or apply <c>[Resource(Key = ...)]</c> to a partial
/// type and let the generator emit this half.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself (the curiously-recurring parameter).</typeparam>
public interface IResource<TSelf>
    where TSelf : IResource<TSelf>
{
    /// <summary>Projects the resource key for <paramref name="instance"/>.</summary>
    static abstract ResourceKey KeyFor(TSelf instance);
}
