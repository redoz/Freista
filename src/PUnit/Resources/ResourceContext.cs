using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PUnit.Model;

namespace PUnit;

/// <summary>
/// The imperative resource surface hanging off <see cref="ScenarioContext.Resources"/>. In C1 it is a
/// pure tracer: each lifecycle verb resolves the resource's identity and records a
/// <see cref="ResourceEffect"/> for the report's resource lane — no locking yet (that arrives in C2,
/// when the verbs also park a token in the scenario scope). The API is async end to end so call sites
/// (<c>await ctx.Resources.Create(user)</c>) stay stable across phases.
/// </summary>
public sealed class ResourceContext
{
    readonly ConcurrentQueue<ResourceEffect> _effects = new();
    readonly ResourceIdentityResolver _resolver;
    readonly TimeProvider _timeProvider;
    readonly string _stepId;
    readonly string _stepDisplayName;

    /// <summary>Creates a tracer bound to one step, resolving identities via <paramref name="resolver"/>.</summary>
    public ResourceContext(
        string stepId,
        string stepDisplayName,
        ResourceIdentityResolver resolver,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _stepId = stepId;
        _stepDisplayName = stepDisplayName;
        _resolver = resolver;
        _timeProvider = timeProvider;
    }

    /// <summary>Effects recorded by this step's verbs, in call order.</summary>
    public IReadOnlyList<ResourceEffect> Effects => _effects.ToArray();

    /// <summary>
    /// Registers a key selector for <typeparamref name="T"/> (resolver chain link 2), for resource
    /// types you can't annotate with <c>[ResourceKey]</c>/<c>KeyFor</c>.
    /// </summary>
    public void Identify<T>(Func<T, ResourceKey> selector) => _resolver.Identify(selector);

    /// <summary>Records bringing a new <paramref name="resource"/> into existence (exclusive).</summary>
    public ValueTask Create<T>(T resource)
        where T : notnull
        => Record(LifecycleVerb.Create, _resolver.Resolve(resource), resource);

    /// <summary>Records fetching an existing resource of type <typeparamref name="T"/> by key (shared, no instance).</summary>
    public ValueTask Load<T>(ResourceKey key)
        where T : notnull
        => Record(LifecycleVerb.Load, new ResourceIdentity(typeof(T), key), data: null);

    /// <summary>Records a shared use of an in-scope <paramref name="resource"/>.</summary>
    public ValueTask Read<T>(T resource)
        where T : notnull
        => Record(LifecycleVerb.Read, _resolver.Resolve(resource), resource);

    /// <summary>Records a mutation of an existing <paramref name="resource"/> (exclusive).</summary>
    public ValueTask Edit<T>(T resource)
        where T : notnull
        => Record(LifecycleVerb.Edit, _resolver.Resolve(resource), resource);

    /// <summary>Records removing a <paramref name="resource"/> (exclusive).</summary>
    public ValueTask Delete<T>(T resource)
        where T : notnull
        => Record(LifecycleVerb.Delete, _resolver.Resolve(resource), resource);

    ValueTask Record(LifecycleVerb verb, ResourceIdentity identity, object? data)
    {
        _effects.Enqueue(new ResourceEffect
        {
            Verb = verb,
            Identity = identity,
            Data = data,
            StepId = _stepId,
            StepDisplayName = _stepDisplayName,
            Timestamp = _timeProvider.GetUtcNow(),
        });
        return ValueTask.CompletedTask;
    }
}
