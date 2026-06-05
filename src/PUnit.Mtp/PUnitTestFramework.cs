// CA1033: the ITestFramework / IDataProducer members are implemented explicitly so PUnit's own
// SessionUid-keyed worker API (CreateTestSession/OnDiscover/...) is the visible surface; the class
// is intentionally left unsealed so later phases (and tests) can override the discover/run bodies.
#pragma warning disable CA1033 // Interface methods should be callable by child types

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Model;
using PUnit.Scheduling;
using ITestPlatformTestFramework = Microsoft.Testing.Platform.Extensions.TestFramework.ITestFramework;

namespace PUnit.Mtp;

/// <summary>
/// PUnit's own Microsoft.Testing.Platform <see cref="ITestPlatformTestFramework"/>. It owns the
/// session lifecycle and the discover-vs-run dispatch, so each scenario step can be reported as a
/// first-class MTP test node (instead of being folded onto a single scenario node, as happened
/// when PUnit rode xUnit as an extension).
/// </summary>
/// <remarks>
/// <para>
/// Modeled on xUnit.net's <c>TestPlatformTestFramework</c> but without xUnit's project / config /
/// reporter / serialization machinery: PUnit's <c>ScenarioScheduler</c> already owns execution, so
/// this shell only has to bridge MTP's request pipeline to it.
/// </para>
/// <para>
/// This type is public because the escape-hatch bootstrap (<see cref="PUnitTestApplication.RunAsync"/>)
/// registers it; the discovery and run bodies (<see cref="OnDiscoverAsync"/> /
/// <see cref="OnExecuteAsync"/>) are filled in by later phases. The interface methods are thin
/// wrappers over the <see cref="SessionUid"/>-keyed worker methods so they can be unit-tested
/// without constructing MTP's internal session-context types.
/// </para>
/// </remarks>
public class PUnitTestFramework :
    IExtension, ITestPlatformTestFramework, IDataProducer
{
    /// <summary>Stable extension UID for PUnit's MTP test framework.</summary>
    internal const string ExtensionUid = "punit.mtp.testframework";

    readonly ConcurrentDictionary<string, byte> sessions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public string Uid => ExtensionUid;

    /// <inheritdoc/>
    public string Version => typeof(PUnitTestFramework).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <inheritdoc/>
    public string DisplayName => "PUnit";

    /// <inheritdoc/>
    public string Description => "PUnit scenario test framework for Microsoft.Testing.Platform";

    /// <inheritdoc/>
    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "Required by the Microsoft.Testing.Platform IDataProducer.DataTypesProduced contract.")]
    public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];

    Task<CreateTestSessionResult> ITestPlatformTestFramework.CreateTestSessionAsync(CreateTestSessionContext context)
        => CreateTestSession(context.SessionUid);

    Task<CloseTestSessionResult> ITestPlatformTestFramework.CloseTestSessionAsync(CloseTestSessionContext context)
        => CloseTestSession(context.SessionUid);

    async Task ITestPlatformTestFramework.ExecuteRequestAsync(ExecuteRequestContext context)
    {
        var sessionUid = context.Request.Session.SessionUid;

        if (context.Request is DiscoverTestExecutionRequest discoverRequest)
        {
            await OnDiscoverAsync(
                sessionUid,
                discoverRequest.Filter,
                context.MessageBus,
                context.Complete,
                context.CancellationToken).ConfigureAwait(false);
        }
        else if (context.Request is RunTestExecutionRequest runRequest)
        {
            await OnExecuteAsync(
                sessionUid,
                runRequest.Filter,
                context.MessageBus,
                context.Complete,
                context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens a session. Fails if the UID is already in progress.</summary>
    public Task<CreateTestSessionResult> CreateTestSession(SessionUid sessionUid)
    {
        if (!sessions.TryAdd(sessionUid.Value, 0))
        {
            return Task.FromResult(new CreateTestSessionResult
            {
                IsSuccess = false,
                ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    "Attempted to reuse session UID '{0}' already in progress",
                    sessionUid.Value),
            });
        }

        return Task.FromResult(new CreateTestSessionResult { IsSuccess = true });
    }

    /// <summary>Closes a previously opened session. Fails if the UID is unknown.</summary>
    public Task<CloseTestSessionResult> CloseTestSession(SessionUid sessionUid)
    {
        if (!sessions.TryRemove(sessionUid.Value, out _))
        {
            return Task.FromResult(new CloseTestSessionResult
            {
                IsSuccess = false,
                ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    "Attempted to close unknown session UID '{0}'",
                    sessionUid.Value),
            });
        }

        return Task.FromResult(new CloseTestSessionResult { IsSuccess = true });
    }

    /// <summary>
    /// Discover entry point keyed on <see cref="SessionUid"/> so it can be driven directly in
    /// tests. Guards that the session is active, then defers to <see cref="OnDiscoverAsync"/>.
    /// </summary>
    public ValueTask OnDiscover(
        SessionUid sessionUid,
        ITestExecutionFilter? filter,
        IMessageBus messageBus,
        Action operationComplete,
        CancellationToken cancellationToken)
    {
        EnsureSession(sessionUid, "discovery");
        return OnDiscoverAsync(sessionUid, filter, messageBus, operationComplete, cancellationToken);
    }

    /// <summary>
    /// Run entry point keyed on <see cref="SessionUid"/> so it can be driven directly in tests.
    /// Guards that the session is active, then defers to <see cref="OnExecuteAsync"/>.
    /// </summary>
    public ValueTask OnExecute(
        SessionUid sessionUid,
        ITestExecutionFilter? filter,
        IMessageBus messageBus,
        Action operationComplete,
        CancellationToken cancellationToken)
    {
        EnsureSession(sessionUid, "execution");
        return OnExecuteAsync(sessionUid, filter, messageBus, operationComplete, cancellationToken);
    }

    /// <summary>
    /// Performs discovery for the session: enumerates every scenario registered in
    /// <see cref="ScenarioRegistry"/>, lowers it via its factory, and publishes one
    /// <see cref="TestNodeUpdateMessage"/> per step node (see <see cref="PUnitDiscoverer"/>).
    /// Step nodes only — no parent scenario node (design decision ①).
    /// </summary>
    protected virtual async ValueTask OnDiscoverAsync(
        SessionUid sessionUid,
        ITestExecutionFilter? filter,
        IMessageBus messageBus,
        Action operationComplete,
        CancellationToken cancellationToken)
    {
        // A null/no-op filter discovers everything (so --list-tests enumerates every step); a
        // TestNodeUidListFilter restricts discovery to the named nodes, matching xUnit's discovery
        // sink. The same parsing backs both discover and run so a single-step uid resolves identically.
        var uids = ReadUidFilter(filter);

        foreach (var methodName in ScenarioRegistry.RegisteredMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ScenarioRegistry.TryGet(methodName, out var factory) || factory is null)
            {
                continue;
            }

            var definition = factory();
            foreach (var node in PUnitDiscoverer.BuildNodes(definition))
            {
                if (uids is not null && !uids.Contains(node.Uid.Value))
                {
                    continue;
                }

                await messageBus
                    .PublishAsync(this, new TestNodeUpdateMessage(sessionUid, node))
                    .ConfigureAwait(false);
            }
        }

        operationComplete();
    }

    /// <summary>
    /// Runs the requested tests for the session via the <see cref="PUnitRunLoop"/>: the filter is
    /// reduced to the distinct scenarios it selects, each is run once through a
    /// <see cref="ScenarioScheduler"/>, and every executed step is published (siblings light up).
    /// </summary>
    protected virtual async ValueTask OnExecuteAsync(
        SessionUid sessionUid,
        ITestExecutionFilter? filter,
        IMessageBus messageBus,
        Action operationComplete,
        CancellationToken cancellationToken)
    {
        var uids = ReadUidFilter(filter);
        var loop = new PUnitRunLoop(EnumerateRegisteredScenarios);

        await loop.RunAsync(sessionUid, uids, messageBus, this, cancellationToken).ConfigureAwait(false);

        operationComplete();
    }

    /// <summary>
    /// Reduces an MTP execution filter to the set of step uids to run, or <see langword="null"/> to
    /// run everything. A <see cref="TestNodeUidListFilter"/> carries an explicit uid list; a null or
    /// no-op filter means "run all". Unknown filter kinds are rejected so a silent partial run can't
    /// masquerade as success.
    /// </summary>
    static HashSet<string>? ReadUidFilter(ITestExecutionFilter? filter) => filter switch
    {
        null => null,
#pragma warning disable TPEXP // NopFilter is an experimental Microsoft.Testing.Platform API.
        NopFilter => null,
#pragma warning restore TPEXP
        TestNodeUidListFilter uidFilter =>
            uidFilter.TestNodeUids.Select(u => u.Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
        _ => throw new ArgumentException(
            string.Format(
                CultureInfo.CurrentCulture,
                "Unsupported execution filter type '{0}'.",
                filter.GetType().FullName),
            nameof(filter)),
    };

    /// <summary>Lowers every scenario registered in <see cref="ScenarioRegistry"/> to its definition.</summary>
    static IEnumerable<ScenarioDefinition> EnumerateRegisteredScenarios()
    {
        foreach (var methodName in ScenarioRegistry.RegisteredMethods)
        {
            if (ScenarioRegistry.TryGet(methodName, out var factory) && factory is not null)
            {
                yield return factory();
            }
        }
    }

    void EnsureSession(SessionUid sessionUid, string requestKind)
    {
        if (!sessions.ContainsKey(sessionUid.Value))
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Attempted to run {0} request against unknown session UID '{1}'",
                    requestKind,
                    sessionUid.Value),
                nameof(sessionUid));
        }
    }
}
