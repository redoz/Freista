namespace PUnit.Reporting;

/// <summary>A subscriber to the run-event stream. The bus awaits each call serially.</summary>
public interface IRunEventSink
{
    /// <summary>Handle one event. May be async; the bus awaits it before the next sink/event.</summary>
    ValueTask PublishAsync(RunEvent evt);
}
