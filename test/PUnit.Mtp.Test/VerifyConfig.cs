using System.Runtime.CompilerServices;

namespace PUnit.Mtp.Test;

public static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize() => Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
}
