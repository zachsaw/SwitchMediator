namespace Mediator.Switch;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorOrderAttribute : Attribute
{
    public int Order { get; }

    public PipelineBehaviorOrderAttribute(int order)
    {
        Order = order;
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorResponseAdaptorAttribute : Attribute
{
    public Type GenericsType { get; }

    public PipelineBehaviorResponseAdaptorAttribute(Type genericsType)
    {
        GenericsType = genericsType;
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequestHandlerAttribute : Attribute
{
    public Type HandlerType { get; }

    public RequestHandlerAttribute(Type handlerType)
    {
        HandlerType = handlerType;
    }
}
