namespace PUnit;

/// <summary>Return/method role: the step produces a <b>new</b> resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class CreatesAttribute : Attribute;

/// <summary>Return/method role: the step returns an <b>existing</b> resource it loaded (shared in C2).</summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class LoadsAttribute : Attribute;

/// <summary>Parameter role: the step only reads the resource (shared in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ReadsAttribute : Attribute;

/// <summary>
/// Parameter role: the produced resource keeps a durable reference to this one (aggregation; shared).
/// Records a <see cref="LifecycleVerb.Reference"/> effect and, paired with the step's
/// <c>[Creates]</c>/<c>[Edits]</c> subject, a lineage edge subject→target in the report. The edge is
/// attributed to the step's SINGLE created/edited resource; a step that creates or edits more than
/// one resource forms no lineage edge for its referenced inputs.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ReferencesAttribute : Attribute;

/// <summary>
/// Parameter role: the step consumes/uses-up this resource into the one it produces (composition;
/// shared in C1, exclusive in C2). Records a <see cref="LifecycleVerb.Consume"/> effect and, paired
/// with the step's <c>[Creates]</c>/<c>[Edits]</c> subject, a lineage edge subject→target in the
/// report. The edge is attributed to the step's SINGLE created/edited resource; a step that creates
/// or edits more than one resource forms no lineage edge for its consumed inputs.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ConsumesAttribute : Attribute;

/// <summary>Parameter or return/method role: the step mutates the resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class EditsAttribute : Attribute;

/// <summary>Parameter role: the step removes the resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DeletesAttribute : Attribute;
