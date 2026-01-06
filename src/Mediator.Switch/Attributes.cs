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

[Obsolete("SwitchMediator no longer needs this as it can now infer the wrapper type starting from v1.16.0.")]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorResponseAdaptorAttribute(Type genericsType) : Attribute
{
    public Type GenericsType { get; } = genericsType;
}