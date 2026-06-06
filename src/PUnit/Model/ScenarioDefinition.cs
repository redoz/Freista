namespace PUnit.Model;

/// <summary>
/// A fully lowered scenario: its metadata plus the ordered execution graph. Emitted by the
/// generator and handed to the scheduler.
/// </summary>
public sealed class ScenarioDefinition
{
    /// <summary>Stable, runner-independent id for the scenario.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>Human-readable scenario name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Fully-qualified name of the originating <c>[Scenario]</c> method.</summary>
    public required string MethodName { get; init; }

    /// <summary>Optional display name for the scenario's declaring class (from a <c>[DisplayName]</c>
    /// attribute); when null the runner uses the real type name.</summary>
    public string? ClassDisplayName { get; init; }

    /// <summary>Source file of the scenario method, if known.</summary>
    public string? SourceFile { get; init; }

    /// <summary>1-based source line of the scenario method, or 0 if unknown.</summary>
    public int SourceLine { get; init; }

    /// <summary>Scenario-wide timeout, or null for none.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The graph nodes in source order.</summary>
    public required IReadOnlyList<ScenarioNode> Nodes { get; init; }

    /// <summary>
    /// Verifies graph invariants the scheduler relies on: every dependency references an existing
    /// node, no node depends on itself, and the graph is acyclic.
    /// </summary>
    /// <exception cref="InvalidOperationException">A dependency is invalid or the graph has a cycle.</exception>
    public void Validate()
    {
        var count = Nodes.Count;

        for (var i = 0; i < count; i++)
        {
            var node = Nodes[i];
            foreach (var dep in node.DependsOn)
            {
                if (dep < 0 || dep >= count)
                {
                    throw new InvalidOperationException(
                        $"Step {node.Index} ('{node.OperationName}') depends on out-of-range node {dep}.");
                }

                if (dep == node.Index)
                {
                    throw new InvalidOperationException(
                        $"Step {node.Index} ('{node.OperationName}') depends on itself.");
                }
            }
        }

        // DFS cycle detection over the index-addressed dependency graph (0 = unvisited,
        // 1 = on stack, 2 = done).
        var state = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (state[i] == 0 && HasCycle(i, state))
            {
                throw new InvalidOperationException(
                    $"Scenario '{DisplayName}' has a dependency cycle involving step {i}.");
            }
        }
    }

    private bool HasCycle(int index, int[] state)
    {
        state[index] = 1;
        foreach (var dep in Nodes[index].DependsOn)
        {
            if (state[dep] == 1)
            {
                return true;
            }

            if (state[dep] == 0 && HasCycle(dep, state))
            {
                return true;
            }
        }

        state[index] = 2;
        return false;
    }
}
