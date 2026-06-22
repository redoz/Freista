using System.Runtime.CompilerServices;

namespace Freista.Mtp.Test;

public static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize() => Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
}
