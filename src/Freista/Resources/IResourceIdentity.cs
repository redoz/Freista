namespace Freista;

/// <summary>
/// Opt-in interface for resources whose key is computed at runtime (link 3 of the resolver chain),
/// for types that can't express a static <see cref="IResource{TSelf}.KeyFor"/>.
/// </summary>
public interface IResourceIdentity
{
    /// <summary>Computes this instance's resource key.</summary>
    ResourceKey GetResourceKey();
}
