using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PUnit;

/// <summary>
/// <c>GetAwaiter</c> extensions that make a scenario's explicit parallel forms directly
/// awaitable C#: a tuple of tasks awaits to a tuple of results, and an array of tasks awaits to
/// an array of results. The elements are already-running (hot) tasks, so awaiting joins them via
/// <see cref="Task.WhenAll(System.Threading.Tasks.Task[])"/> rather than serializing.
///
/// The generator still lowers these forms into individual step nodes; these awaiters exist so the
/// authored scenario method remains honest, compilable, runnable C#.
/// </summary>
[SuppressMessage("Performance", "CA1849:Call async methods when in an async method",
    Justification = "Each result is read only after Task.WhenAll has completed the tasks, so .Result does not block.")]
public static class ScenarioAwaiters
{
    public static TaskAwaiter<T[]> GetAwaiter<T>(this Task<T>[] tasks)
        => Task.WhenAll(tasks).GetAwaiter();

    public static TaskAwaiter<(T1, T2)> GetAwaiter<T1, T2>(
        this (Task<T1> first, Task<T2> second) tasks)
        => Combine(tasks.first, tasks.second).GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3)> GetAwaiter<T1, T2, T3>(
        this (Task<T1>, Task<T2>, Task<T3>) tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3).GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3, T4)> GetAwaiter<T1, T2, T3, T4>(
        this (Task<T1>, Task<T2>, Task<T3>, Task<T4>) tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4).GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3, T4, T5)> GetAwaiter<T1, T2, T3, T4, T5>(
        this (Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>) tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5).GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3, T4, T5, T6)> GetAwaiter<T1, T2, T3, T4, T5, T6>(
        this (Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>, Task<T6>) tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6)
            .GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3, T4, T5, T6, T7)> GetAwaiter<T1, T2, T3, T4, T5, T6, T7>(
        this (Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>, Task<T6>, Task<T7>) tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6,
            tasks.Item7).GetAwaiter();

    public static TaskAwaiter<(T1, T2, T3, T4, T5, T6, T7, T8)>
        GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8>(
            this (Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>, Task<T6>, Task<T7>, Task<T8>)
                tasks)
        => Combine(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6,
            tasks.Item7, tasks.Item8).GetAwaiter();

    static async Task<(T1, T2)> Combine<T1, T2>(Task<T1> t1, Task<T2> t2)
    {
        await Task.WhenAll(t1, t2).ConfigureAwait(false);
        return (t1.Result, t2.Result);
    }

    static async Task<(T1, T2, T3)> Combine<T1, T2, T3>(Task<T1> t1, Task<T2> t2, Task<T3> t3)
    {
        await Task.WhenAll(t1, t2, t3).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result);
    }

    static async Task<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(
        Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4)
    {
        await Task.WhenAll(t1, t2, t3, t4).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result, t4.Result);
    }

    static async Task<(T1, T2, T3, T4, T5)> Combine<T1, T2, T3, T4, T5>(
        Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4, Task<T5> t5)
    {
        await Task.WhenAll(t1, t2, t3, t4, t5).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result, t4.Result, t5.Result);
    }

    static async Task<(T1, T2, T3, T4, T5, T6)> Combine<T1, T2, T3, T4, T5, T6>(
        Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4, Task<T5> t5, Task<T6> t6)
    {
        await Task.WhenAll(t1, t2, t3, t4, t5, t6).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result, t4.Result, t5.Result, t6.Result);
    }

    static async Task<(T1, T2, T3, T4, T5, T6, T7)> Combine<T1, T2, T3, T4, T5, T6, T7>(
        Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4, Task<T5> t5, Task<T6> t6, Task<T7> t7)
    {
        await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result, t4.Result, t5.Result, t6.Result, t7.Result);
    }

    static async Task<(T1, T2, T3, T4, T5, T6, T7, T8)> Combine<T1, T2, T3, T4, T5, T6, T7, T8>(
        Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4, Task<T5> t5, Task<T6> t6, Task<T7> t7,
        Task<T8> t8)
    {
        await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8).ConfigureAwait(false);
        return (t1.Result, t2.Result, t3.Result, t4.Result, t5.Result, t6.Result, t7.Result,
            t8.Result);
    }
}
