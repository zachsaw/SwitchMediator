using FluentResults;
using FluentValidation;
using Mediator.Switch;

namespace Sample;

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
[RequestHandler(typeof(GetUserRequestHandler))]
public class GetUserRequest : IRequest<Result<string>>, IAuditableRequest
{
    public int UserId { get; }
    public DateTime Timestamp { get; }
    public GetUserRequest(int userId) => (UserId, Timestamp) = (userId, DateTime.Now);
}

[RequestHandler(typeof(CreateOrderRequestHandler))]
public class CreateOrderRequest : IRequest<Result<int>>, ITransactionalRequest
{
    public string Product { get; }
    public Guid TransactionId { get; }
    public CreateOrderRequest(string product) => (Product, TransactionId) = (product, Guid.NewGuid());
}

[RequestHandler(typeof(GetVersionRequestHandler))]
public class GetVersionRequest : IRequest<Result<VersionedResponse>>
{
}

public class VersionedResponse : IVersionedResponse
{
    public int Version { get; set; }
}

// Notification type
public class UserLoggedInEvent : INotification
{
    public int UserId { get; }
    public UserLoggedInEvent(int userId) => UserId = userId;
}

// Handlers
public class GetUserRequestHandler : IRequestHandler<GetUserRequest, Result<string>>
{
    public async Task<Result<string>> Handle(GetUserRequest request) => $"User {request.UserId} at {request.Timestamp}";
}

public class CreateOrderRequestHandler : IRequestHandler<CreateOrderRequest, Result<int>>
{
    public async Task<Result<int>> Handle(CreateOrderRequest request) => 42; // Simulated order ID
}

public class GetVersionRequestHandler : IRequestHandler<GetVersionRequest, Result<VersionedResponse>>
{
    public async Task<Result<VersionedResponse>> Handle(GetVersionRequest request) => new VersionedResponse{ Version = 42 };
}

public class UserLoggedInLogger : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification) => Console.WriteLine($"Logged: User {notification.UserId} logged in.");
}

public class UserLoggedInAnalytics : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification) => Console.WriteLine($"Analytics: User {notification.UserId} tracked.");
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
    where TResponse : class
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next)
    {
        Console.WriteLine($"Logging: Handling {typeof(TRequest).Name}");
        var response = await next();
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

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next)
    {
        if (_validator != null)
        {
            var result = await _validator.ValidateAsync(request);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }
        return await next();
    }
}

public interface IVersionedResponse
{
    int Version { get; set; }
}

[PipelineBehaviorOrder(3), PipelineBehaviorResponseAdaptor(typeof(Result<>))]
public class AuditBehaviorInner<TRequest, TResponse> : IPipelineBehavior<TRequest, Result<TResponse>>
    where TRequest : IAuditableRequest
    where TResponse : IVersionedResponse
{
    public async Task<Result<TResponse>> Handle(TRequest request, RequestHandlerDelegate<Result<TResponse>> next)
    {
        Console.WriteLine($"Audit: Processing request at {request.Timestamp}");
        var result = await next();
        if (result.IsSuccess)
            return Result.Fail("failed");
        
        var versionedResponse = result.Value;
        versionedResponse.Version++;
        Console.WriteLine($"Result = {versionedResponse.Version}");
        Console.WriteLine($"Audit: Completed request at {request.Timestamp}");
        return result;
    }
}

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next)
    {
        Console.WriteLine($"Transaction: Starting with ID {request.TransactionId}");
        var response = await next();
        Console.WriteLine($"Transaction: Completed with ID {request.TransactionId}");
        return response;
    }
}
