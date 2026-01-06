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
