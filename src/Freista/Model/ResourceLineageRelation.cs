using Freista;

namespace Freista.Model;

/// <summary>
/// One explicitly-declared lineage relation recorded by a step: the produced/edited
/// <see cref="Subject"/> holds a <see cref="Kind"/> relationship to <see cref="Target"/>.
/// </summary>
public sealed record ResourceLineageRelation
{
    /// <summary>The produced/edited resource (a <c>[Creates]</c>/<c>[Edits]</c> subject).</summary>
    public required ResourceIdentity Subject { get; init; }

    /// <summary>The referenced/consumed resource.</summary>
    public required ResourceIdentity Target { get; init; }

    /// <summary><see cref="LifecycleVerb.Reference"/> or <see cref="LifecycleVerb.Consume"/>.</summary>
    public required LifecycleVerb Kind { get; init; }
}
