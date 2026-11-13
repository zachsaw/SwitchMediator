namespace Mediator.Switch;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequestHandlerAttribute(Type handlerType) : Attribute
{
    public Type HandlerType { get; } = handlerType;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SwitchMediatorAttribute : Attribute
{
    /// <summary>
    /// Controls how the generated mediator caches the handlers and pipeline behaviors it resolves.
    /// Defaults to <see cref="HandlerCache.LazyDictionary"/>.
    /// </summary>
    public HandlerCache HandlerCache { get; set; } = HandlerCache.LazyDictionary;

    /// <summary>
    /// Controls the type the generated mediator constructor takes to resolve its dependencies.
    /// Defaults to <see cref="MediatorServiceProvider.Abstraction"/>.
    /// </summary>
    public MediatorServiceProvider ServiceProvider { get; set; } = MediatorServiceProvider.Abstraction;
}

/// <summary>
/// How the generated mediator receives the container it resolves its dependencies from.
/// </summary>
public enum MediatorServiceProvider
{
    /// <summary>
    /// Constructor takes <see cref="ISwitchMediatorServiceProvider"/> (the default). Container
    /// agnostic: any container can implement that interface.
    /// </summary>
    Abstraction,

    /// <summary>
    /// Constructor takes <see cref="System.IServiceProvider"/> directly. Removes the
    /// <see cref="ISwitchMediatorServiceProvider"/> service from the resolution graph, which is
    /// one less scoped service and one less allocation per scope. Intended for
    /// Microsoft.Extensions.DependencyInjection, where <see cref="System.IServiceProvider"/> is
    /// resolved to the current scope automatically.
    /// </summary>
    Direct,
}


/// <summary>
/// How the generated mediator keeps the handlers and pipeline behaviors it resolves.
/// </summary>
public enum HandlerCache
{
    /// <summary>
    /// No cache: every component is resolved from the service provider on each send.
    /// The mediator instance stays tiny (~24 bytes), which is the cheapest option for scopes
    /// that send a single message. Note that this changes the effective lifetime of handlers
    /// registered with <c>ServiceLifetime.Transient</c>: they are created per send instead of
    /// once per mediator instance.
    /// </summary>
    None,

    /// <summary>
    /// One nullable field per component, the behaviour of earlier versions. Fastest per send, but the
    /// mediator instance grows with the number of request types: one field per handler and per
    /// (behavior, request) pair, allocated on every scope.
    /// </summary>
    Fields,

    /// <summary>
    /// A <c>Dictionary&lt;Type, object?&gt;</c> created with the mediator and populated on demand.
    /// The footprint scales with what a scope actually uses instead of with the number of types.
    /// </summary>
    Dictionary,

    /// <summary>
    /// A <c>Dictionary&lt;Type, object?&gt;</c> created on first use and populated on demand.
    /// Same as <see cref="Dictionary"/> without paying for the dictionary in scopes that never send.
    /// </summary>
    LazyDictionary,
}
