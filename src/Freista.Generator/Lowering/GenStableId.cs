using System.Globalization;
using System.Text;

namespace Freista.Generator.Lowering;

/// <summary>
/// Compile-time id generation, byte-for-byte identical to <c>Freista.StableId</c> so the
/// ids the generator bakes into the manifest match the runtime/reference implementation.
/// </summary>
internal static class GenStableId
{
    public static string ForScenario(string methodFullName) => Hash(methodFullName);

    public static string ForStep(string scenarioId, string stepKey) => Hash(scenarioId + "/" + stepKey);

    public static string Hash(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
