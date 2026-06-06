using System.Reflection;
using Microsoft.Testing.Platform.Extensions.Messages;
using PUnit.Model;

namespace PUnit.Mtp;

/// <summary>
/// Derives a <see cref="TestMethodIdentifierProperty"/> from a scenario's fully-qualified method
/// name so runners (VS Test Explorer, the VSTest bridge) group step nodes under their real
/// namespace → class → scenario-method. Without this property a node has no method identity and the
/// runner buckets it under <c>&lt;Empty Namespace&gt;</c> / <c>&lt;Empty Class&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScenarioDefinition.MethodName"/> is emitted by the generator as
/// <c>{declaringTypeFqn}.{methodSimpleName}</c>, where <c>declaringTypeFqn</c> is
/// <c>{namespace}.{typeSimpleName}</c> (an empty namespace for a top-level type). The split is the
/// inverse: the last dotted segment is the method, the segment before it the simple type name, and
/// anything ahead of that the namespace. Nested types (rare for scenario hosts) fold the outer type
/// name into the namespace segment — still a stable, non-empty grouping, just not a perfect class
/// split; the generator would have to emit the parts separately to do better.
/// </para>
/// <para>
/// The assembly name is resolved once from the entry assembly (the running test app), matching what
/// xunit.v3's MTP bridge stamps; it is metadata only and does not affect node identity (the
/// <c>{ScenarioId}:{StepId}</c> uid does).
/// </para>
/// </remarks>
internal static class ScenarioTestIdentity
{
    private static readonly string AssemblyFullName = Assembly.GetEntryAssembly()?.FullName ?? string.Empty;

    /// <summary>The VSTest spelling of a <see langword="void"/> return type (matches xunit.v3's MTP bridge).</summary>
    private const string VoidReturnTypeName = "System.Void";

    /// <summary>
    /// Builds the method-identity property for a scenario: namespace and type are derived from
    /// <paramref name="methodFullName"/> (the scenario method's FQN), but the method node is the human
    /// <paramref name="scenarioDisplayName"/> so a runner groups steps under the scenario name. When
    /// <paramref name="classDisplayName"/> is non-empty it overrides the derived type name.
    /// </summary>
    public static TestMethodIdentifierProperty Create(
        string methodFullName, string scenarioDisplayName, string? classDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioDisplayName);
        Split(methodFullName, out var @namespace, out var typeName, out _);

        var type = string.IsNullOrEmpty(classDisplayName) ? typeName : classDisplayName!;

        // Positional ctor args (assembly, namespace, type, method, method-arity, parameter-types,
        // return-type) — matching xunit.v3's MTP bridge. Scenarios are non-generic, parameterless
        // (the DSL drives them), so arity 0 / no parameters / void.
        return new TestMethodIdentifierProperty(
            AssemblyFullName,
            @namespace,
            type,
            scenarioDisplayName,
            0,
            [],
            VoidReturnTypeName);
    }

    /// <summary>
    /// Splits a <c>{namespace}.{type}.{method}</c> name into its parts. The namespace (and, for a
    /// bare name, the type) come back empty rather than throwing.
    /// </summary>
    internal static void Split(string methodFullName, out string @namespace, out string typeName, out string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodFullName);

        var lastDot = methodFullName.LastIndexOf('.');
        if (lastDot < 0)
        {
            // No declaring type in the name; treat the whole thing as the method.
            @namespace = string.Empty;
            typeName = string.Empty;
            methodName = methodFullName;
            return;
        }

        methodName = methodFullName.Substring(lastDot + 1);
        var declaringType = methodFullName.Substring(0, lastDot);

        var typeDot = declaringType.LastIndexOf('.');
        if (typeDot < 0)
        {
            // Top-level type: no namespace.
            @namespace = string.Empty;
            typeName = declaringType;
        }
        else
        {
            @namespace = declaringType.Substring(0, typeDot);
            typeName = declaringType.Substring(typeDot + 1);
        }
    }
}
