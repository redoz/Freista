using System.Globalization;
using System.Text;
using Microsoft.Testing.Platform.Extensions.Messages;

namespace PUnit.Mtp;

/// <summary>
/// Opt-in tracing of every <see cref="TestNode"/> PUnit publishes to the MTP message bus (discovery
/// and each step lifecycle update). Off by default and zero-cost; set <c>FREISTA_NODE_DEBUG</c> to a
/// file path, or to <c>1</c>/<c>stderr</c>/<c>console</c> for stderr. Use it to see the exact Uid,
/// display name, state, and property bag of each node — in particular to compare the in-progress
/// node (which a runner groups) against the finished node (which can collapse) when diagnosing how a
/// runner builds its tree.
/// </summary>
internal static class NodeDiagnostics
{
    private static readonly string? Sink = Environment.GetEnvironmentVariable("FREISTA_NODE_DEBUG");
    private static readonly object FileLock = new();

    /// <summary>Whether tracing is enabled (the <c>FREISTA_NODE_DEBUG</c> environment variable is set).</summary>
    public static bool Enabled => !string.IsNullOrEmpty(Sink);

    /// <summary>Traces one published node under the given <paramref name="phase"/> (e.g. "discover", "run").</summary>
    public static void Log(string phase, TestNode node)
    {
        if (!Enabled)
        {
            return;
        }

        Write(Describe(phase, node));
    }

    /// <summary>Traces a plain message under the given <paramref name="phase"/> (e.g. "report-sink-failure").</summary>
    public static void Log(string phase, string message)
    {
        if (!Enabled)
        {
            return;
        }

        Write($"[freista-node] {phase} {message}");
    }

    private static string Describe(string phase, TestNode node)
    {
        var state = node.Properties.OfType<TestNodeStateProperty>().FirstOrDefault()?.GetType().Name ?? "(none)";

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"[freista-node] {phase} uid='{node.Uid.Value}' name='{node.DisplayName}' state={state}");

        var identity = node.Properties.OfType<TestMethodIdentifierProperty>().FirstOrDefault();
        if (identity is not null)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $" identity=(asm='{identity.AssemblyFullName}' ns='{identity.Namespace}' type='{identity.TypeName}' method='{identity.MethodName}')");
        }
        else
        {
            builder.Append(" identity=(none)");
        }

        var props = string.Join(",", node.Properties.AsEnumerable().Select(p => p.GetType().Name));
        builder.Append(CultureInfo.InvariantCulture, $" count={node.Properties.Count} props=[{props}]");

        return builder.ToString();
    }

    private static void Write(string line)
    {
        if (string.Equals(Sink, "1", StringComparison.Ordinal)
            || string.Equals(Sink, "stderr", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Sink, "console", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(line);
            return;
        }

        try
        {
            lock (FileLock)
            {
                File.AppendAllText(Sink!, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Diagnostics are best-effort; never let tracing break a run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
