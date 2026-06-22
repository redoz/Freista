using Freista;
using Xunit;

namespace Freista.Test;

/// <summary>
/// The runtime supplies <c>GetAwaiter</c> extensions so a scenario's tuple/array parallel forms
/// are honest, directly-runnable C#. These verify result aggregation, ordering, arity, and that
/// already-running elements are joined rather than serialized.
/// </summary>
public class AwaiterTests
{
    [Fact]
    public async Task Tuple_of_two_returns_both_results_in_order()
    {
        var (a, b) = await (Task.FromResult(1), Task.FromResult("x"));

        Assert.Equal(1, a);
        Assert.Equal("x", b);
    }

    [Fact]
    public async Task Tuple_of_three_returns_all_results()
    {
        var (a, b, c) = await (Task.FromResult(1), Task.FromResult(2), Task.FromResult(3));

        Assert.Equal((1, 2, 3), (a, b, c));
    }

    [Fact]
    public async Task Array_returns_all_results_in_order()
    {
        var result = await new[] { Task.FromResult(10), Task.FromResult(20), Task.FromResult(30) };

        Assert.Equal(new[] { 10, 20, 30 }, result);
    }

    [Fact]
    public async Task Tuple_joins_already_running_elements_without_serializing()
    {
        var gate = new TaskCompletionSource();
        var started = 0;

        async Task<int> Element(int value)
        {
            Interlocked.Increment(ref started);
            await gate.Task;
            return value;
        }

        // Both hot tasks reach their first await before the tuple is awaited.
        var first = Element(1);
        var second = Element(2);
        Assert.Equal(2, started);

        gate.SetResult();
        var (a, b) = await (first, second);

        Assert.Equal((1, 2), (a, b));
    }

    [Fact]
    public async Task Tuple_propagates_element_exception()
    {
        static Task<int> Boom() => Task.FromException<int>(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await (Task.FromResult(1), Boom()));

        Assert.Equal("boom", ex.Message);
    }
}
