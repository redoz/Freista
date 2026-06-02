namespace PUnit.Model;

/// <summary>
/// Read access to the outputs of already-completed steps, handed to a node's invoke delegate.
/// Generated code resolves a step's arguments with <see cref="Get{T}"/>, passing the index of the
/// producing node.
/// </summary>
public interface IStepInputs
{
    /// <summary>Gets the output produced by the step at <paramref name="producerIndex"/>.</summary>
    T Get<T>(int producerIndex);
}
