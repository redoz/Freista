using System.Runtime.CompilerServices;

namespace Freista.Generator.Test;

/// <summary>Wires up Verify's source-generator support and disables interactive diff tooling.</summary>
public static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
        VerifySourceGenerators.Initialize();
    }
}
