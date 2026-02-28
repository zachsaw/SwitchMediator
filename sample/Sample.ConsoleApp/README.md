## SwitchMediator Sample Console Application

This sample application demonstrates various technical capabilities of the **SwitchMediator** library:

*   **Request/Response Handling:** Shows the basic pattern using `IRequest<TResponse>` and their corresponding `IRequestHandler` implementations.

    ```csharp
    // Request definition
    public class CreateOrderRequest : IRequest<int>, ITransactionalRequest { /* ... */ }

    // Handler implementation
    public class CreateOrderRequestHandler : IRequestHandler<CreateOrderRequest, int>
    {
        public async Task<int> Handle(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        { /* ... handler logic ... */ }
    }
    ```

*   **Publish/Subscribe Notifications:** Demonstrates broadcasting events (`INotification`) to multiple handlers (`INotificationHandler`).

    ```csharp
    // Notification definition
    public class UserLoggedInEvent : INotification { /* ... */ }

    // Handler implementation (one of potentially many)
    public class UserLoggedInLogger : INotificationHandler<UserLoggedInEvent>
    {
        public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default)
        { /* ... logging logic ... */ }
    }
    ```

*   **Zero-Allocation Dispatch with `IValueRequestHandler` / `IValueNotificationHandler`:** Use `IValueMediator` / `IValueSender` / `IValuePublisher` for the optimized dispatch path. ValueTask-based handlers eliminate `Task<T>` heap allocation, and ValueTask notification dispatch achieves zero allocation in the mediator dispatch layer.

    ```csharp
    // Fast synchronous handler — no Task object allocated in the handler itself
    public class FastStatusCheckHandler : IValueRequestHandler<FastStatusCheckRequest, bool>
    {
        public ValueTask<bool> Handle(FastStatusCheckRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    // Zero-allocation notification handler
    public class ServerStartedLogger : IValueNotificationHandler<ServerStartedEvent>
    {
        public ValueTask Handle(ServerStartedEvent notification, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("Server started!");
            return ValueTask.CompletedTask;
        }
    }

    // Dispatching via IValueSender / IValuePublisher
    var valueSender = serviceProvider.GetRequiredService<IValueSender>();
    var valuePublisher = serviceProvider.GetRequiredService<IValuePublisher>();

    bool ok = await valueSender.Send(new FastStatusCheckRequest());
    await valuePublisher.Publish(new ServerStartedEvent());
    ```

    > **Design Note:** When using `IValueRequestHandler`, all applicable pipeline behaviors for that request must implement `IValuePipelineBehavior` (not `IPipelineBehavior`). The **SMD002** analyzer enforces this at compile time. A clean way to separate pipelines is via marker interfaces (e.g., `IValidatable`) that constrain which behaviors apply to which requests.

*   **Handler Discovery via Attribute:** Uses the `[RequestHandler]` attribute on request types to link them to their specific handler implementation, allowing easy navigation within the IDE. This attribute now also accepts `IValueRequestHandler` handlers.

    ```csharp
    [RequestHandler(typeof(FastStatusCheckHandler))] // Works for IValueRequestHandler too
    public class FastStatusCheckRequest : IRequest<bool> { }
    ```

*   **Pipeline Behaviors (`IPipelineBehavior`):** Illustrates the middleware concept for requests:
    *   **Generic Behaviors:** Applied to all requests matching constraints.
        ```csharp
        // Applies to any TRequest/TResponse where TResponse is a class
        public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            where TRequest : notnull
            where TResponse : class
        { /* ... */ }
        ```
    *   **Constrained Behaviors:** Applied only to requests implementing specific marker interfaces.
        ```csharp
        // Only applies if TRequest implements IAuditableRequest
        public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            where TRequest : IAuditableRequest
        { /* ... */ }
        ```
    *   **Separation via Marker Interfaces:** Adding a marker (e.g., `IValidatable`) lets you control which behaviors apply to Task vs. ValueTask request pipelines without triggering **SMD002**.
        ```csharp
        // ValidationBehavior only applies to requests that implement IValidatable
        public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            where TRequest : notnull, IValidatable
        { /* ... */ }

        // These requests get validation
        public class GetUserRequest : IRequest<Result<User>>, IAuditableRequest, IValidatable { }

        // This ValueTask request bypasses ValidationBehavior (no IValidatable)
        public class FastStatusCheckRequest : IRequest<bool> { }
        ```
    *   **Explicit Ordering:** Uses `[PipelineBehaviorOrder(N)]` to control the execution sequence. Behaviors without the attribute run last.
        ```csharp
        [PipelineBehaviorOrder(1)] // Runs first
        public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { /* ... */ }

        [PipelineBehaviorOrder(2)] // Runs second
        public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { /* ... */ }

        // Runs last (default order int.MaxValue)
        public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { /* ... */ }
        ```
    *   **Dependency Injection:** Behaviors can receive dependencies via their constructor.
        ```csharp
        public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        {
            private readonly IValidator<TRequest>? _validator; // Injected dependency

            public ValidationBehavior(IValidator<TRequest>? validator = null) // Constructor injection
            {
                _validator = validator;
            }
            // ...
        }
        ```

*   **Notification Pipeline Behaviors (Resilience & Retries):** Demonstrates how to wrap notification handlers with middleware using `INotificationPipelineBehavior<TNotification>`.
    *   **Isolation:** Unlike request pipelines, notification behaviors wrap *each individual handler execution*.
    *   **Resilience (Swallowing Exceptions):** Catch exceptions from a specific handler so others continue running.
    *   **Retries (Polly Integration):** Because the behavior wraps the handler, you can easily implement retry policies using libraries like Polly.

    **Example: Using Polly for Retries**
    ```csharp
    public class PollyRetryBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
        where TNotification : IRetryableNotification
    {
        // Define a simple retry policy (e.g., retry 3 times)
        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<Exception>()
            .RetryAsync(3);

        public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
        {
            // Execute the handler (next) within the Polly policy
            await _retryPolicy.ExecuteAsync(async (ct) => await next(ct), cancellationToken);
        }
    }
    ```

*   **FluentValidation Integration:** The `ValidationBehavior` seamlessly integrates with FluentValidation by resolving `IValidator<TRequest>` from the DI container and executing validation within the pipeline. Registration is simplified using extension methods.

    ```csharp
    // Inside ValidationBehavior.Handle:
    if (_validator != null)
    {
        var result = await _validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors); // Throw on validation failure
        }
    }

    // In Program.Main (DI setup):
    services.AddValidatorsFromAssembly(typeof(Program).Assembly); // Auto-register validators
    ```

*   **FluentResults Integration & Response Adaptation:**
    *   Requests can return wrapped results like `FluentResults.Result<T>`.
        ```csharp
        public class GetUserRequest : IRequest<Result<User>> { /* ... */ } // Returns Result<User>
        ```
    *   Behaviors can intelligently work with these wrapped types using `[PipelineBehaviorResponseAdaptor]` to specify the wrapper type and generic constraints to access the inner value.
        ```csharp
        // This behavior operates on requests that return Result<TResponse>,
        // where TResponse must implement IVersionedResponse.
        [PipelineBehaviorOrder(4), PipelineBehaviorResponseAdaptor(typeof(Result<>))]
        public class VersionIncrementingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, Result<TResponse>>
            where TRequest : notnull
            where TResponse : IVersionedResponse // Constraint on the *inner* type
        {
            public async Task<Result<TResponse>> Handle(TRequest request, RequestHandlerDelegate<Result<TResponse>> next, CancellationToken cancellationToken = default)
            {
                var result = await next(); // result is Result<TResponse>
                if (result.IsSuccess)
                {
                    var versionedResponse = result.Value; // Access the inner TResponse value
                    versionedResponse.Version++;
                    // ... logic using versionedResponse ...
                }
                return result;
            }
        }
        ```

*   **Dependency Injection Setup:** Shows configuration using `SwitchMediator.Extensions.Microsoft.DependencyInjection`, including assembly scanning for automatic registration of handlers and behaviors. `IValueMediator`, `IValueSender`, and `IValuePublisher` are registered automatically.

    ```csharp
    // In Program.Main:
    var services = new ServiceCollection();
    // ... other registrations ...
    services.AddMediator<SwitchMediator>(op =>
    {
        op.KnownTypes = SwitchMediator.KnownTypes;
        op.ServiceLifetime = ServiceLifetime.Singleton;
        op.OrderNotificationHandlers<UserLoggedInEvent>(
            typeof(UserLoggedInLogger),
            typeof(UserLoggedInAnalytics)
        );
    });

    // IValueSender and IValuePublisher are also available after AddMediator
    var valueSender = serviceProvider.GetRequiredService<IValueSender>();
    var valuePublisher = serviceProvider.GetRequiredService<IValuePublisher>();
    ```

*   **Notification Handler Ordering:** Demonstrates explicit control over the execution order for handlers of a specific notification type.

    ```csharp
    // In Program.Main (DI setup):
    services.AddMediator<SwitchMediator>(op =>
    {
        // ... other options ...
        op.OrderNotificationHandlers<UserLoggedInEvent>( // Specify order for UserLoggedInEvent
            typeof(UserLoggedInLogger), // Runs first
            typeof(UserLoggedInAnalytics) // Runs second
        );
    });
    ```

*   **Core Abstractions:** Uses the `ISender` and `IPublisher` interfaces to interact with the mediator, obtained via DI.

    ```csharp
    // In Program.RunSample:
    private static async Task RunSample(ISender sender, IPublisher publisher, IValueSender valueSender, IValuePublisher valuePublisher)
    {
        // Send a request-response message (Task-based)
        var userResult = await sender.Send(new GetUserRequest(123));

        // Send via ValueTask path (reduced allocation)
        bool ok = await valueSender.Send(new FastStatusCheckRequest());

        // Publish a notification message (Task-based)
        await publisher.Publish(new UserLoggedInEvent(123));

        // Publish via ValueTask path (zero dispatch allocation)
        await valuePublisher.Publish(new ServerStartedEvent());
    }
    ```