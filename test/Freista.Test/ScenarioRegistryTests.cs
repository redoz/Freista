using Freista;
using Freista.Model;
using Xunit;

namespace Freista.Test;

/// <summary>
/// Generated code registers each scenario's definition factory keyed by the originating method's
/// full name; the xUnit discoverer looks the factory up by the same key.
/// </summary>
public class ScenarioRegistryTests
{
    private static ScenarioDefinition Empty(string method) => new()
    {
        ScenarioId = "id",
        DisplayName = method,
        MethodName = method,
        Nodes = [],
    };

    [Fact]
    public void Registered_factory_is_retrievable_by_method_name()
    {
        var key = "Ns.Type." + nameof(Registered_factory_is_retrievable_by_method_name);
        ScenarioRegistry.Register(key, () => Empty(key));

        Assert.True(ScenarioRegistry.TryGet(key, out var factory));
        Assert.Equal(key, factory!().MethodName);
    }

    [Fact]
    public void Missing_key_returns_false()
    {
        Assert.False(ScenarioRegistry.TryGet("Ns.Type.NeverRegistered", out var factory));
        Assert.Null(factory);
    }
}
