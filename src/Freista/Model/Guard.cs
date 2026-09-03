namespace Freista.Model;

/// <summary>
/// A branch condition a node is gated on: the node runs only when the node at
/// <see cref="ConditionIndex"/> passed AND its evaluated condition equals <see cref="WhenValue"/>
/// (<see langword="true"/> for the <c>if</c> arm, <see langword="false"/> for the <c>else</c> arm).
/// Nested <c>if</c>s stack guards; all of them must hold.
/// </summary>
public readonly record struct Guard(int ConditionIndex, bool WhenValue);
