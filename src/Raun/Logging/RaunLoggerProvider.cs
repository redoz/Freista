using Microsoft.Extensions.Logging;

namespace Raun;

/// <summary>
/// Routes <see cref="ILogger"/> writes onto the step that is currently running, via
/// <see cref="ScenarioContext.Current"/>. Register it with any
/// <see cref="ILoggingBuilder"/> — including the system under test's, when it runs in-process — and
/// its log lines are attributed to the step that provoked them, appearing in that step's MTP
/// standard output and in the HTML report alongside the step's own logs.
/// </summary>
/// <remarks>
/// Writes made when no step is running (background work, a host still starting after the scenario
/// finished) are dropped: there is no step to attribute them to, and inventing one would be a lie.
/// Level filtering is deliberately not implemented here — that is
/// <see cref="ILoggingBuilder"/>'s job.
/// </remarks>
public sealed class RaunLoggerProvider : ILoggerProvider
{
    /// <summary>Creates a logger writing to whichever step is running when each write happens.</summary>
    public ILogger CreateLogger(string categoryName) => new RaunLogger(categoryName);

    /// <summary>Nothing to release: loggers hold no state beyond their category.</summary>
    public void Dispose()
    {
    }
}

/// <summary>
/// An <see cref="ILogger"/> writing to a step's log lines.
/// </summary>
/// <remarks>
/// Two binding modes, deliberately different:
/// <list type="bullet">
///   <item>with <paramref name="bound"/> null (the provider's loggers) the destination is resolved
///   at <b>write</b> time from <see cref="ScenarioContext.Current"/> — which is what lets a service
///   resolve its logger once at start-up and still have each later write land on the step that
///   caused it;</item>
///   <item>with <paramref name="bound"/> set (<see cref="ScenarioContext.GetLogger{T}"/>) writes go
///   to that context, because a logger asked of a specific step should not silently retarget if it
///   escapes to another one.</item>
/// </list>
/// </remarks>
internal sealed class RaunLogger(string category, ScenarioContext? bound = null) : ILogger
{
    /// <summary>Scopes are not represented in step logs; returns null.</summary>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <summary>Everything except <see cref="LogLevel.None"/> is enabled; filtering belongs to the
    /// logging builder, not to this provider.</summary>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        // Resolved per write for the provider's loggers: the ambient step is the whole point.
        if ((bound ?? ScenarioContext.Current) is not { } context)
        {
            return;
        }

        var message = formatter(state, exception);
        context.Log(exception is null
            ? $"{logLevel} | {category} | {message}"
            : $"{logLevel} | {category} | {message} | {exception}");
    }
}
