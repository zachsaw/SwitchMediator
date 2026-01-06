using FluentValidation;
using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FullPipeline;

[SwitchMediator]
public partial class TestMediator;

#pragma warning disable CS1998

public interface IAuditableRequest
{
    DateTime Timestamp { get; }
}

public interface ITransactionalRequest
{
    Guid TransactionId { get; }
}

// Request types
public class GetUserRequest : IRequest<string>, IAuditableRequest
{
    public int UserId { get; }
    public DateTime Timestamp { get; }
    public GetUserRequest(int userId) => (UserId, Timestamp) = (userId, DateTime.Now);
}

public class CreateOrderRequest : IRequest<int>, ITransactionalRequest
{
    public string Product { get; }
    public Guid TransactionId { get; }
    public CreateOrderRequest(string product) => (Product, TransactionId) = (product, Guid.NewGuid());
}

// Notification type
public class UserLoggedInEvent : INotification
{
    public int UserId { get; }
    public UserLoggedInEvent(int userId) => UserId = userId;
}

// Handlers
public class GetUserRequestHandler : IRequestHandler<GetUserRequest, string>
{
    public async Task<string> Handle(GetUserRequest request, CancellationToken cancellationToken = default) => $"User {request.UserId} at {request.Timestamp}";
}

public class CreateOrderRequestHandler : IRequestHandler<CreateOrderRequest, int>
{
    public async Task<int> Handle(CreateOrderRequest request, CancellationToken cancellationToken = default) => 42; // Simulated order ID
}

public class UserLoggedInLogger : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default) => Console.WriteLine($"Logged: User {notification.UserId} logged in.");
}

public class UserLoggedInAnalytics : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default) => Console.WriteLine($"Analytics: User {notification.UserId} tracked.");
}

// FluentValidation validators
public class GetUserRequestValidator : AbstractValidator<GetUserRequest>
{
    public GetUserRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("UserId must be positive");
    }
}

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Product).NotEmpty().WithMessage("Product cannot be empty");
    }
}

// Generic pipeline behaviors
[PipelineBehaviorOrder(1)]
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Logging: Handling {typeof(TRequest).Name}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Logging: Handled {typeof(TRequest).Name}");
        return response;
    }
}

[PipelineBehaviorOrder(2)]
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IValidator<TRequest>? _validator;

    public ValidationBehavior(IValidator<TRequest>? validator = null)
    {
        _validator = validator;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (_validator != null)
        {
            var result = await _validator.ValidateAsync(request);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }
        return await next(cancellationToken);
    }
}

[PipelineBehaviorOrder(3)]
public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuditableRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Audit: Processing request at {request.Timestamp}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Audit: Completed request at {request.Timestamp}");
        return response;
    }
}

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Transaction: Starting with ID {request.TransactionId}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Transaction: Completed with ID {request.TransactionId}");
        return response;
    }
}