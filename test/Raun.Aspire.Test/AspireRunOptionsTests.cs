using Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Raun.Aspire.Test;

/// <summary>
/// Unit coverage for the parts of Raun.Aspire that need no container runtime. Actually standing
/// up an AppHost is the sample's job.
/// </summary>
public class AspireRunOptionsTests
{
    private sealed class Marker;

    [Fact]
    public void WaitFor_is_additive_across_calls()
    {
        // Additive so a suite can compose its configuration from several helpers rather than being
        // forced into a single call site.
        var options = new AspireRunOptions();

        options.WaitFor("postgres");
        options.WaitFor("api", "worker");

        Assert.Equal(["postgres", "api", "worker"], options.WaitForResources);
    }

    [Fact]
    public void WaitFor_defaults_to_nothing()
    {
        Assert.Empty(new AspireRunOptions().WaitForResources);
    }

    [Fact]
    public void StartupTimeout_defaults_to_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new AspireRunOptions().StartupTimeout);
    }

    [Fact]
    public void Service_registrations_accumulate_and_all_are_applied()
    {
        var options = new AspireRunOptions();
        options.Services(s => s.AddSingleton("first"));
        options.Services(s => s.AddSingleton(new Marker()));

        var services = new ServiceCollection();
        options.ApplyServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Equal("first", provider.GetRequiredService<string>());
        Assert.NotNull(provider.GetRequiredService<Marker>());
    }

    [Fact]
    public void Aspire_throws_an_actionable_message_when_no_application_is_registered()
    {
        var ctx = new ScenarioContext("s", "s", services: null, CancellationToken.None);

        var ex = Assert.Throws<InvalidOperationException>(ctx.Aspire);

        Assert.Contains("RaunAspire.RunAsync", ex.Message, StringComparison.Ordinal);
        Assert.Contains("RaunGenerateProgram", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aspire_throws_when_services_exist_but_hold_no_application()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new ScenarioContext("s", "s", provider, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(ctx.Aspire);
    }

    [Fact]
    public void Aspire_returns_the_registered_application()
    {
        // DistributedApplication cannot be constructed here without an AppHost, so the registration
        // is exercised through the same resolution path with a stand-in provider.
        var app = (DistributedApplication?)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DistributedApplication));
        using var provider = new ServiceCollection().AddSingleton(app!).BuildServiceProvider();
        var ctx = new ScenarioContext("s", "s", provider, CancellationToken.None);

        Assert.Same(app, ctx.Aspire());
    }
}
