namespace Freista;

/// <summary>
/// Return/method role: the step produces a <b>new</b> resource (exclusive).
/// <see cref="References"/>/<see cref="Consumes"/> name the input parameters (each via <c>nameof</c>,
/// or <see cref="Subject.Return"/>) that flow into the produced resource, recording a lineage relation
/// from the produced resource to each. Each named target also takes the matching shared
/// <see cref="LifecycleVerb.Reference"/>/<see cref="LifecycleVerb.Consume"/> effect — naming a target
/// is its access declaration, so the target stays a bare parameter.
/// </summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class CreatedAttribute : Attribute
{
    /// <summary>Inputs the produced resource keeps a durable reference to (aggregation; shared).</summary>
    public string[] References { get; set; } = [];

    /// <summary>Inputs the produced resource consumes/uses-up (composition; shared).</summary>
    public string[] Consumes { get; set; } = [];
}

/// <summary>
/// Return/method role: the step returns an <b>existing</b> resource it loaded (shared).
/// Carries the same lineage <see cref="References"/>/<see cref="Consumes"/> surface as
/// <see cref="CreatedAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class LoadedAttribute : Attribute
{
    /// <summary>Inputs the loaded resource keeps a durable reference to (aggregation; shared).</summary>
    public string[] References { get; set; } = [];

    /// <summary>Inputs the loaded resource consumes/uses-up (composition; shared).</summary>
    public string[] Consumes { get; set; } = [];
}

/// <summary>Parameter role: the step only reads the resource (shared).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ReadAttribute : Attribute;

/// <summary>
/// Parameter or return/method role: the step mutates the resource (exclusive). In a producing
/// position the edited resource may carry lineage <see cref="References"/>/<see cref="Consumes"/>
/// naming the inputs that flow into it.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class EditedAttribute : Attribute
{
    /// <summary>Inputs the edited resource keeps a durable reference to (aggregation; shared).</summary>
    public string[] References { get; set; } = [];

    /// <summary>Inputs the edited resource consumes/uses-up (composition; shared).</summary>
    public string[] Consumes { get; set; } = [];
}

/// <summary>Parameter role: the step removes the resource (exclusive).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DeletedAttribute : Attribute;
