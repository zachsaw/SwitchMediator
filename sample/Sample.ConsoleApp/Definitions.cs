using FluentResults;
using FluentValidation;
using Mediator.Switch;

namespace Sample.ConsoleApp;

#pragma warning disable CS1998

// --- Interfaces & Markers ---

public interface IAuditableRequest
{
    DateTime Timestamp { get; }
}

public interface ITransactionalRequest
{
    Guid TransactionId { get; }
}

public interface IVersionedResponse
{
    int Version { get; set; }
}

// Marker for notifications that should not crash the entire publish loop on failure
public interface ISilentFailureNotification : INotification;

// Marker for notifications that should be retried on failure
public interface IRetryableNotification : INotification;


// --- Requests ---

[RequestHandler(typeof(GetUserRequestHandler))]
public class GetUserRequest(int userId) : IRequest<Result<User>>, IAuditableRequest
{
    public int UserId { get; } = userId;
    public DateTime Timestamp { get; } = DateTime.Now;
}

[RequestHandler(typeof(CreateOrderRequestHandler))]
public record CreateOrderRequest(string Product) : IRequest<int>, ITransactionalRequest
{
    public string Product { get; } = Product;
    public Guid TransactionId { get; } = Guid.NewGuid();
}

[RequestHandler(typeof(AnimalRequestHandler))]
public abstract record Animal : IRequest<Unit>
{
    public abstract string AnimalType { get; }
}

public record Dog : Animal
{
    public override string AnimalType => "Dog";
}

public record Cat : Animal
{
    public override string AnimalType => "Cat";
}


// --- Notifications ---

public class UserLoggedInEvent(int userId) : INotification
{
    public int UserId { get; } = userId;
}

public class DerivedUserLoggedInEvent(int userId) : UserLoggedInEvent(userId);

// Notification for Resilience Demo (Swallow Exception)
public class SystemAlert(string message) : ISilentFailureNotification
{
    public string Message { get; } = message;
}

// Notification for Retry Demo (Simulate Polly)
public class UnstableServiceEvent(string jobId) : IRetryableNotification
{
    public string JobId { get; } = jobId;
}


// --- Response Models ---

public class User : IVersionedResponse
{
    public int UserId { get; set; }
    public string Description { get; set; } = "";
    public int Version { get; set; }
}


// --- Request Handlers ---

public class GetUserRequestHandler(IPublisher publisher) : IRequestHandler<GetUserRequest, Result<User>>
{
    public async Task<Result<User>> Handle(GetUserRequest request, CancellationToken cancellationToken = default)
    {
        await publisher.Publish(new UserLoggedInEvent(request.UserId), cancellationToken);
        return new User
        {
            UserId = request.UserId,
            Description = $"User {request.UserId} at {request.Timestamp}",
            Version = 50
        };
    }
}

public class CreateOrderRequestHandler : IRequestHandler<CreateOrderRequest, int>
{
    public async Task<int> Handle(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        42; // Simulated order ID
}

public class AnimalRequestHandler : IRequestHandler<Animal, Unit>
{
    public async Task<Unit> Handle(Animal request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(request.AnimalType);
        return Unit.Value;
    }
}


// --- Notification Handlers ---

public class UserLoggedInLogger : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default) =>
        Console.WriteLine($"Logged: User {notification.UserId} logged in.");
}

public class UserLoggedInAnalytics : INotificationHandler<UserLoggedInEvent>
{
    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default) =>
        Console.WriteLine($"Analytics: User {notification.UserId} tracked.");
}

// Resilience Demo: This handler simulates a critical failure
public class FailingSystemAlertHandler : INotificationHandler<SystemAlert>
{
    public Task Handle(SystemAlert notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[FailingHandler] Attempting to send SMS for: {notification.Message}");
        throw new Exception("SMS Gateway Connection Timeout");
    }
}

// Resilience Demo: This handler should still run even if the previous one failed
public class LoggingSystemAlertHandler : INotificationHandler<SystemAlert>
{
    public Task Handle(SystemAlert notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[LoggingHandler] Alert written to disk: {notification.Message}");
        return Task.CompletedTask;
    }
}

// Retry Demo: This handler fails intermittently
public class UnstableServiceHandler : INotificationHandler<UnstableServiceEvent>
{
    // Static counter to simulate transient failure across calls
    private static int _attempts;

    public Task Handle(UnstableServiceEvent notification, CancellationToken cancellationToken)
    {
        _attempts++;
        Console.WriteLine($"[UnstableHandler] Processing Job {notification.JobId} (Attempt #{_attempts})...");

        if (_attempts <= 2)
        {
            Console.WriteLine($"[UnstableHandler] >>> Connection Failed!");
            throw new IOException("Transient Network Error");
        }

        Console.WriteLine($"[UnstableHandler] >>> Success!");
        return Task.CompletedTask;
    }
}


// --- Validators ---

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


// --- Request Pipeline Behaviors ---

[PipelineBehaviorOrder(1)]
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
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
public class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest>? validator = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (validator != null)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
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
        var result = await next(cancellationToken);
        Console.WriteLine($"Audit: Completed request at {request.Timestamp}");
        return result;
    }
}

[PipelineBehaviorOrder(4)]
public class VersionIncrementingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, Result<TResponse>>
    where TRequest : notnull
    where TResponse : IVersionedResponse
{
    public async Task<Result<TResponse>> Handle(TRequest request, RequestHandlerDelegate<Result<TResponse>> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("VersionTagging: Starting");
        var result = await next(cancellationToken);
        if (!result.IsSuccess)
            return result;

        var versionedResponse = result.Value;
        versionedResponse.Version++;
        if (versionedResponse.Version > 100)
            return Result.Fail("Max Version is 100"); // simulate a failed result
        Console.WriteLine($"Version: {versionedResponse.Version}");
        Console.WriteLine("VersionTagging: Completed");
        return result;
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


// --- Notification Pipeline Behaviors ---

// Resilience Behavior: Swallows exceptions from handlers
public class ResilientNotificationBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : ISilentFailureNotification
{
    public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        try
        {
            await next(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Resilience] Swallowed exception from a handler: {ex.Message}");
        }
    }
}

// Retry Behavior: Simulates Polly retry logic
public class RetryNotificationBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : IRetryableNotification
{
    public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        int attempts = 0;

        while (true)
        {
            try
            {
                attempts++;
                // In a real app using Polly, this would be:
                // await _retryPolicy.ExecuteAsync(async (ct) => await next(ct), cancellationToken);
                await next(cancellationToken);
                break; // Success
            }
            catch (Exception ex)
            {
                if (attempts >= maxRetries)
                {
                    Console.WriteLine($"[Retry] Failed after {attempts} attempts. Giving up. Error: {ex.Message}");
                    throw; // Bubble up the exception after max retries
                }

                Console.WriteLine($"[Retry] Caught error: '{ex.Message}'. Retrying... ({attempts}/{maxRetries})");
                // Simple backoff
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}