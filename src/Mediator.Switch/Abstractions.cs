namespace Mediator.Switch;

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
}

public interface IPublisher
{
    Task Publish(INotification notification);
}

public interface IMediator : ISender, IPublisher;

public interface IRequest<out TResponse>;

public interface INotification;

public interface IRequestHandler<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request);
}

public interface INotificationHandler<in TNotification>
{
    Task Handle(TNotification notification);
}

public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, Func<TRequest, Task<TResponse>> next);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PipelineBehaviorOrderAttribute : Attribute
{
    public int Order { get; }

    public PipelineBehaviorOrderAttribute(int order)
    {
        Order = order;
    }
}