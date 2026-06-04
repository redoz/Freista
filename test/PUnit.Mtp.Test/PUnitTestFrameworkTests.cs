using System.Reflection;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Mtp;
using Xunit;

namespace PUnit.Mtp.Test;

/// <summary>
/// Phase 2 behavioral tests for the MTP <c>ITestFramework</c> shell: session create/close
/// correctness and request dispatch (discover vs run routing). Discovery/run bodies land in
/// later phases; here we assert the shell's contract and that <c>ExecuteRequestAsync</c> routes
/// each request kind to the right handler and always completes the operation.
/// </summary>
public static class PUnitTestFrameworkTests
{
    public class SessionManagement
    {
        [Fact]
        public async Task Create_then_close_session_succeeds()
        {
            var uid = new SessionUid("session-1");
            var framework = new PUnitTestFramework();

            var createResult = await framework.CreateTestSession(uid);
            Assert.True(createResult.IsSuccess);

            var closeResult = await framework.CloseTestSession(uid);
            Assert.True(closeResult.IsSuccess);
        }

        [Fact]
        public async Task Creating_same_session_twice_fails()
        {
            var uid = new SessionUid("dup");
            var framework = new PUnitTestFramework();

            var first = await framework.CreateTestSession(uid);
            var second = await framework.CreateTestSession(uid);

            Assert.True(first.IsSuccess);
            Assert.False(second.IsSuccess);
            Assert.Contains("dup", second.ErrorMessage);
        }

        [Fact]
        public async Task Closing_unknown_session_fails()
        {
            var framework = new PUnitTestFramework();

            var result = await framework.CloseTestSession(new SessionUid("never-opened"));

            Assert.False(result.IsSuccess);
            Assert.Contains("never-opened", result.ErrorMessage);
        }

        [Fact]
        public async Task Re_closing_a_session_fails()
        {
            var uid = new SessionUid("once");
            var framework = new PUnitTestFramework();

            await framework.CreateTestSession(uid);
            var firstClose = await framework.CloseTestSession(uid);
            var secondClose = await framework.CloseTestSession(uid);

            Assert.True(firstClose.IsSuccess);
            Assert.False(secondClose.IsSuccess);
        }

        [Fact]
        public async Task Discovery_against_unknown_session_throws_and_does_not_complete()
        {
            var framework = new PUnitTestFramework();
            var completed = false;

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await framework.OnDiscover(
                    new SessionUid("ghost"),
                    filter: null,
                    new SpyMessageBus(),
                    () => completed = true,
                    CancellationToken.None));

            Assert.False(completed);
            Assert.Contains("ghost", ex.Message);
        }

        [Fact]
        public async Task Execution_against_unknown_session_throws_and_does_not_complete()
        {
            var framework = new PUnitTestFramework();
            var completed = false;

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await framework.OnExecute(
                    new SessionUid("ghost"),
                    filter: null,
                    new SpyMessageBus(),
                    () => completed = true,
                    CancellationToken.None));

            Assert.False(completed);
            Assert.Contains("ghost", ex.Message);
        }
    }

    public class RequestDispatch
    {
        [Fact]
        public async Task DiscoverRequest_routes_to_discover_and_completes()
        {
            var uid = new SessionUid("disp-d");
            var framework = new RecordingTestFramework();
            await framework.CreateTestSession(uid);

            var completed = false;
            var context = MtpContextFactory.ExecuteRequest(
                MtpContextFactory.DiscoverRequest(uid),
                new SpyMessageBus(),
                () => completed = true,
                CancellationToken.None);

            await ((ITestFramework)framework).ExecuteRequestAsync(context);

            Assert.Equal(1, framework.DiscoverCalls);
            Assert.Equal(0, framework.ExecuteCalls);
            Assert.True(completed);
        }

        [Fact]
        public async Task RunRequest_routes_to_execute_and_completes()
        {
            var uid = new SessionUid("disp-r");
            var framework = new RecordingTestFramework();
            await framework.CreateTestSession(uid);

            var completed = false;
            var context = MtpContextFactory.ExecuteRequest(
                MtpContextFactory.RunRequest(uid),
                new SpyMessageBus(),
                () => completed = true,
                CancellationToken.None);

            await ((ITestFramework)framework).ExecuteRequestAsync(context);

            Assert.Equal(0, framework.DiscoverCalls);
            Assert.Equal(1, framework.ExecuteCalls);
            Assert.True(completed);
        }

        [Fact]
        public async Task Discover_request_carries_the_sessions_uid()
        {
            var uid = new SessionUid("uid-flows");
            var framework = new RecordingTestFramework();
            await framework.CreateTestSession(uid);

            var context = MtpContextFactory.ExecuteRequest(
                MtpContextFactory.DiscoverRequest(uid),
                new SpyMessageBus(),
                () => { },
                CancellationToken.None);

            await ((ITestFramework)framework).ExecuteRequestAsync(context);

            Assert.Equal("uid-flows", framework.LastSessionUid?.Value);
        }
    }

    /// <summary>
    /// A subclass that records which handler the dispatch routed to without running real
    /// discovery/execution (those land in later phases). Lets the dispatch test observe routing.
    /// </summary>
    sealed class RecordingTestFramework : PUnitTestFramework
    {
        public int DiscoverCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public SessionUid? LastSessionUid { get; private set; }

        protected override ValueTask OnDiscoverAsync(
            SessionUid sessionUid,
            ITestExecutionFilter? filter,
            IMessageBus messageBus,
            Action operationComplete,
            CancellationToken cancellationToken)
        {
            DiscoverCalls++;
            LastSessionUid = sessionUid;
            operationComplete();
            return default;
        }

        protected override ValueTask OnExecuteAsync(
            SessionUid sessionUid,
            ITestExecutionFilter? filter,
            IMessageBus messageBus,
            Action operationComplete,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            LastSessionUid = sessionUid;
            operationComplete();
            return default;
        }
    }

    sealed class SpyMessageBus : IMessageBus
    {
        public List<IData> Published { get; } = [];

        public Task PublishAsync(IDataProducer dataProducer, IData data)
        {
            Published.Add(data);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Builds the MTP request/context types whose session-bearing constructors are internal to
    /// Microsoft.Testing.Platform. The request constructors are public but need a
    /// <see cref="TestSessionContext"/>, which (like <c>ClientInfo</c>) only has an internal
    /// constructor — so we reach it via reflection. This mirrors what the platform host does at
    /// runtime; the framework code under test never constructs these itself.
    /// </summary>
    static class MtpContextFactory
    {
        static readonly Assembly Mtp = typeof(ITestExecutionFilter).Assembly;

        static TestSessionContext SessionContext(SessionUid uid)
        {
            var clientInfoType = Mtp.GetType("Microsoft.Testing.Platform.TestHost.ClientInfo")!;
            var client = Activator.CreateInstance(
                clientInfoType,
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: ["punit-test", "1.0"],
                culture: null);

            return (TestSessionContext)Activator.CreateInstance(
                typeof(TestSessionContext),
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: [uid, client],
                culture: null)!;
        }

        public static DiscoverTestExecutionRequest DiscoverRequest(SessionUid uid)
            => new(SessionContext(uid));

        public static RunTestExecutionRequest RunRequest(SessionUid uid)
            => new(SessionContext(uid));

        // TPEXP: ExecuteRequestContext's constructor and IExecuteRequestCompletionNotifier are
        // experimental Microsoft.Testing.Platform APIs. The platform host constructs these at
        // runtime; we build them here only to exercise the framework's dispatch routing.
#pragma warning disable TPEXP
        public static ExecuteRequestContext ExecuteRequest(
            IRequest request,
            IMessageBus messageBus,
            Action onComplete,
            CancellationToken cancellationToken)
            => new(request, messageBus, new CompletionNotifier(onComplete), cancellationToken);

        sealed class CompletionNotifier(Action onComplete) : IExecuteRequestCompletionNotifier
        {
            public void Complete() => onComplete();
        }
#pragma warning restore TPEXP
    }
}
