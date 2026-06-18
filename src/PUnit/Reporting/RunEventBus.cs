namespace PUnit.Reporting;

/// <summary>
/// Fans a <see cref="RunEvent"/> out to child sinks serially, in registration order, awaiting each.
/// A throwing sink is isolated: the bus records its first error in <see cref="Failures"/> and keeps
/// delivering to the remaining sinks and to that sink on later events. A broken report sink must
/// never fail the run or starve the MTP reporter (design §3.A "Failure isolation").
/// </summary>
public sealed class RunEventBus : IRunEventSink
{
    private readonly IReadOnlyList<IRunEventSink> _sinks;
    private readonly Exception?[] _firstError;
    private readonly List<Exception> _failures = [];

    public RunEventBus(IReadOnlyList<IRunEventSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks;
        _firstError = new Exception?[sinks.Count];
    }

    /// <summary>The first error each failed sink raised, in sink order; empty when all sinks held.</summary>
    public IReadOnlyList<Exception> Failures => _failures;

    public async ValueTask PublishAsync(RunEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        for (var i = 0; i < _sinks.Count; i++)
        {
            try
            {
                await _sinks[i].PublishAsync(evt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_firstError[i] is null)
                {
                    _firstError[i] = ex;
                    _failures.Add(ex);
                }
            }
        }
    }
}
