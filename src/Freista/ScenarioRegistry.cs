using System.Collections.Concurrent;
using Freista.Model;

namespace Freista;

/// <summary>
/// Process-wide map from a scenario method's full name to a factory that builds its lowered
/// <see cref="ScenarioDefinition"/>. The generator emits a module initializer that registers each
/// scenario here; the xUnit discoverer resolves definitions by the same key.
/// </summary>
public static class ScenarioRegistry
{
    private static readonly ConcurrentDictionary<string, Func<ScenarioDefinition>> Factories = new();

    /// <summary>Registers (or replaces) the definition factory for a scenario method.</summary>
    public static void Register(string methodFullName, Func<ScenarioDefinition> factory)
        => Factories[methodFullName] = factory;

    /// <summary>Looks up the definition factory for a scenario method.</summary>
    public static bool TryGet(string methodFullName, out Func<ScenarioDefinition>? factory)
        => Factories.TryGetValue(methodFullName, out factory);

    /// <summary>The method names currently registered.</summary>
    public static IReadOnlyCollection<string> RegisteredMethods => [.. Factories.Keys];
}
