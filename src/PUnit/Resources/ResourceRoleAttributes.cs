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

/// <summary>Parameter or return/method role: the step mutates the resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class EditsAttribute : Attribute;

/// <summary>Parameter role: the step removes the resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DeletesAttribute : Attribute;
