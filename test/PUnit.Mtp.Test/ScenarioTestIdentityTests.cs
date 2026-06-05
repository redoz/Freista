using PUnit.Mtp;
using Xunit;

namespace PUnit.Mtp.Test;

/// <summary>
/// Unit tests for the FQN → (namespace, type, method) split that backs the
/// <see cref="Microsoft.Testing.Platform.Extensions.Messages.TestMethodIdentifierProperty"/> the
/// discoverer and reporter attach for runner grouping.
/// </summary>
public class ScenarioTestIdentityTests
{
    [Theory]
    [InlineData("MyApp.Bookings.Book", "MyApp", "Bookings", "Book")]
    [InlineData("My.Deep.Ns.Bookings.Book", "My.Deep.Ns", "Bookings", "Book")]
    [InlineData("Bookings.Book", "", "Bookings", "Book")]   // top-level type, no namespace
    [InlineData("Book", "", "", "Book")]                    // bare name (no declaring type)
    public void Split_separates_namespace_type_and_method(string fqn, string ns, string type, string method)
    {
        ScenarioTestIdentity.Split(fqn, out var actualNamespace, out var actualType, out var actualMethod);

        Assert.Equal(ns, actualNamespace);
        Assert.Equal(type, actualType);
        Assert.Equal(method, actualMethod);
    }

    [Fact]
    public void Create_uses_scenario_display_name_as_method_and_derives_namespace_type_from_fqn()
    {
        var id = ScenarioTestIdentity.Create("MyApp.Bookings.Book", "customer books an appointment");

        Assert.Equal("MyApp", id.Namespace);
        Assert.Equal("Bookings", id.TypeName);
        Assert.Equal("customer books an appointment", id.MethodName);
        Assert.Equal("System.Void", id.ReturnTypeFullName);
        Assert.Empty(id.ParameterTypeFullNames);
    }
}
