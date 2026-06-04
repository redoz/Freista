namespace PUnit.Mtp;

/// <summary>
/// Internal anchor for the PUnit.Mtp assembly. Phase 1 scaffolding: gives the assembly a
/// type and lets the test project verify the project reference and <c>InternalsVisibleTo</c>
/// wiring before the real ITestFramework shell, discovery, reporter, and run loop land in
/// later phases.
/// </summary>
internal static class AssemblyMarker
{
    /// <summary>The simple name of this assembly.</summary>
    public const string AssemblyName = "PUnit.Mtp";
}
