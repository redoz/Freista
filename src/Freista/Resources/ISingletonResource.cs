namespace Freista;

/// <summary>
/// Marker for an ambient singleton resource (e.g. <c>TheSystem</c>): the type itself is the
/// identity, so no key projection is needed. Coupled with <c>[Requires&lt;T&gt;]</c> (C2) or
/// <c>ctx.Resources.Exclusive&lt;T&gt;()</c>.
/// </summary>
/// <typeparam name="TSelf">The implementing singleton type itself.</typeparam>
#pragma warning disable CA1040 // Avoid empty interfaces — intentional marker (cf. IPhase in Phases.cs).
public interface ISingletonResource<TSelf>
    where TSelf : ISingletonResource<TSelf>
{
}
#pragma warning restore CA1040
