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
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request);
}

public interface INotificationHandler<in TNotification>
{
    Task Handle(TNotification notification);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next);
}
