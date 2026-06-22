using Freista.Mtp;
using Xunit;

namespace Freista.Mtp.Test;

/// <summary>
/// Phase 1 scaffold sanity: the Freista.Mtp assembly is referenced, loadable, and its
/// internals are visible to this test project (the wiring later phases — the ITestFramework
/// shell, discovery, reporter, run loop — build on). Replaced/extended as those land.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void FreistaMtp_assembly_is_referenced_and_internals_visible()
    {
        // Touches an internal marker in Freista.Mtp; compiles only if the project reference
        // and InternalsVisibleTo are wired correctly.
        Assert.Equal("Freista.Mtp", AssemblyMarker.AssemblyName);
    }
}
