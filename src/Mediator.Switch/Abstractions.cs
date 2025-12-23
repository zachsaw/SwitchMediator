namespace Mediator.Switch;

public interface ISender
{
    /// <summary>
    /// Sends a request to a single handler and returns the response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the request.</typeparam>
    /// <param name="request">The request object.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A task representing the asynchronous operation, containing the response.</returns>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to multiple handlers.
    /// </summary>
    /// <param name="notification">The notification object.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}

public interface IMediator : ISender, IPublisher;

public interface IRequest<out TResponse>;

public interface INotification;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

public interface INotificationHandler<in TNotification>
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}

public delegate Task NotificationHandlerDelegate();

public interface INotificationPipelineBehavior<in TRequest> where TRequest : notnull
{
    Task Handle(TRequest request, NotificationHandlerDelegate next, CancellationToken cancellationToken = default);
}

public interface ISwitchMediatorServiceProvider
{
    T Get<T>() where T : notnull;
}