using System;

namespace PUnit;

/// <summary>
/// A symbolic resource identity: its CLR <see cref="System.Type"/> paired with its
/// <see cref="ResourceKey"/>. Two effects on the same identity coordinate (and, in C2, share a lock);
/// compared by value.
/// </summary>
public readonly record struct ResourceIdentity(Type Type, ResourceKey Key)
{
    /// <summary>Renders as <c>TypeName:Key</c> (e.g. <c>User:jane@acme.com</c>).</summary>
    public override string ToString() => $"{Type.Name}:{Key}";
}
