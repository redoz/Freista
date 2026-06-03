namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill enabling <c>init</c> accessors and records in this netstandard2.0 source generator.
/// The compiler requires <c>System.Runtime.CompilerServices.IsExternalInit</c> for init-only
/// setters, but that type ships only in .NET 5+.
/// </summary>
internal static class IsExternalInit
{
}
