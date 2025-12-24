# SwitchMediator

[![Build Status](https://img.shields.io/github/actions/workflow/status/zachsaw/SwitchMediator/dotnet.yml?branch=main)](https://github.com/zachsaw/SwitchMediator/actions)
[![NuGet Version](https://img.shields.io/nuget/v/Mediator.Switch.svg)](https://www.nuget.org/packages/Mediator.Switch/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SwitchMediator: A Blazing Fast, Source-Generated Mediator for .NET**

SwitchMediator provides a high-performance (see [benchmark results](benchmark/Mediator.Switch.Benchmark/benchmark_results.md) - **1688x** faster and **117x** less memory allocated on startup, and no performance regression even with >1000 request handlers) implementation of the mediator pattern, offering an API surface familiar to users of popular libraries like [MediatR](https://github.com/jbogard/MediatR). By leveraging **C# Source Generators**, SwitchMediator eliminates runtime reflection for handler discovery and dispatch, instead generating highly optimized `switch` statements at compile time (this is done using a static readonly FrozenDictionary that gets constructed at startup for O(1) execution regardless of the number of request / events you might have). We also want you to <em>**Switch**</em> your <em>**Mediator**</em> to ours, get it? 😉

Aside from performance, SwitchMediator is first and foremost designed to overcome frequent community frustrations with MediatR, addressing factors that have hindered its wider adoption especially due to its less than ideal DX (developer experience).

**The result? Faster execution, improved startup times, step-into debuggability, and compile-time safety.**

---

## Table of Contents

*   [What's New in V2](#whats-new-in-v2)
*   [Why SwitchMediator?](#why-switchmediator)
*   [Key Advantages Over Reflection-Based Mediators](#key-advantages-over-reflection-based-mediators)
*   [Features](#features)
*   [Installation](#installation)
*   [Usage Example](#usage-example)
    *   [DI Setup](#di-setup)
    *   [Sending Requests & Publishing Notifications](#sending-requests--publishing-notifications)
    *   [Notification Pipelines (Resilience & Retries)](#notification-pipelines-resilience--retries)
    *   [Example Output](#example-output)
*   [License](#license)

---

## What's New in V2

### 1. Notification Pipeline Behaviors (`INotificationPipelineBehavior`)
V2 introduces support for pipeline behaviors on Notifications.

Since Requests have a single handler, the pipeline wraps that single execution path. However, Notifications are broadcast to multiple handlers. SwitchMediator wraps **each handler execution independently** in its own pipeline scope.

This enables powerful patterns like **Resilience**: you can write a behavior that catches exceptions from a specific handler, logs them, and swallows them, ensuring that *other* handlers for the same notification still execute.

**The Performance Advantage:**
In reflection-based mediators, wrapping every single event handler in a chain of behaviors creates a massive amount of delegate allocations and closure overhead at runtime. **SwitchMediator generates this "Russian Doll" wrapping code at compile time.** This makes the pipeline structure effectively "free" at runtime—zero allocation overhead for the pipeline construction itself.

### 2. ⚠️ Breaking Change: `next(cancellationToken)`
To further optimize performance and reduce memory allocations, the signature for pipeline delegates has changed. You must now pass the `cancellationToken` explicitly to `next`.

**Why?** This prevents the compiler from creating a closure to capture the `cancellationToken` from the outer scope, significantly reducing allocations in high-throughput scenarios.

**Before (V1):**
```csharp
await next(); // Implicitly captured token, caused allocation
```

**After (V2):**
```csharp
await next(cancellationToken); // Explicit pass, zero allocation
```

---

## Why SwitchMediator?

Traditional mediator implementations often rely on runtime reflection to discover handlers and construct pipelines. While flexible, this approach can introduce overhead:

*   **Runtime Performance Cost:** Reflection and dictionary lookups add latency to every request dispatch.
*   **Startup Delay:** Scanning assemblies and building internal mappings takes time when your application starts.
*   **Debugging Complexity:** Stepping through the mediator logic can involve navigating complex internal library code, delegates, and reflection calls.
*   **Trimming/AOT Challenges:** Reflection can make code less friendly to .NET trimming and Ahead-of-Time (AOT) compilation scenarios.

**SwitchMediator tackles these issues head-on by moving the work to compile time.** The source generator creates explicit C# code with direct handler calls and optimized `switch` statements, offering a "pay-as-you-go" approach where the cost is incurred during build, not at runtime.

## Key Advantages Over Reflection-Based Mediators

*   🚀 **Maximum Performance:** Eliminates runtime reflection and dictionary lookups for dispatch. Uses compile-time `switch` statements and direct method calls. Ideal for performance-critical paths and high-throughput applications.
*   🧐 **Enhanced Debuggability:** You can directly **step into the generated `SwitchMediator.g.cs` file**! See the exact `switch` statement matching your request, observe the explicit nesting of pipeline behavior calls (`await next(...)`), and step directly into your handler code. This provides unparalleled transparency compared to debugging reflection-based dispatch logic.
*   ✅ **Compile-Time Safety:** Handler discovery happens during the build. Missing request handlers result in **build errors**, not runtime exceptions, catching issues earlier in the development cycle.
*   ✂️ **Trimming / AOT Friendly:** Avoids runtime reflection, making the dispatch mechanism inherently more compatible with .NET trimming and AOT compilation. The sample console app runs with PublishAot enabled. (Note: Ensure handlers and dependencies are also trimming/AOT safe).
*   🔍 **Explicitness:** The generated code serves as clear, inspectable documentation of how requests are routed and pipelines are constructed for each message type. Attributes are provided to ease navigating between Request and its handler, as well as to order pipeline behaviors. Ordering of notification handlers can also be explicitly specified.

## Features

*   Request/Response messages (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`)
*   Notification messages (`INotification`, `INotificationHandler<TNotification>`)
*   Pipeline Behaviors (`IPipelineBehavior<TRequest, TResponse>`) for cross-cutting concerns.
*   **[NEW]** Notification Pipeline Behaviors (`INotificationPipelineBehavior<TNotification>`) for per-handler middleware.
*   Native support for Result pattern (e.g. [FluentResults](https://github.com/altmann/FluentResults)).
*   Flexible Pipeline Behavior Ordering via `[PipelineBehaviorOrder(int order)]`.
*   Pipeline Behavior Constraints using standard C# generic constraints (`where TRequest : ...`).
*   Explicit Notification Handler Ordering via DI configuration.
*   Seamless integration with `Microsoft.Extensions.DependencyInjection`.

## Installation

You'll typically need two packages:

1. **`Mediator.Switch.SourceGenerator`:** The SwitchMediator source generator itself, typically in the outermost project where your mediator related classes would reside. The source generator assembly does *not* get packaged with your application.
2. **`Mediator.Switch.Extensions.Microsoft.DependencyInjection`:** (Optional) Provides extension methods for easy registration with the standard .NET DI container. You can always manually register your DI instances.

```bash
dotnet add package Mediator.Switch.SourceGenerator
dotnet add package Mediator.Switch.Extensions.Microsoft.DependencyInjection
```

## Usage Example

Refer to [Sample app](sample/Sample.ConsoleApp) for more information.

### DI Setup

Register SwitchMediator and its dependencies (handlers, behaviors, validators) in your application's composition root (e.g., `Program.cs`).

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mediator.Switch;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using System;
using System.Threading.Tasks;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();

        // --- SwitchMediator Registration ---
        // 1. Register SwitchMediator itself.
        services.AddMediator<SwitchMediator>(op =>
        {
            // 2. Pass compile-time pre-discovered types containing handlers, messages, behaviors for scanning and specify service lifetime.
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
            // 3. Optionally, specify notification handler order.
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(UserLoggedInLogger),
                typeof(UserLoggedInAnalytics)
            );
        });

        // --- Build and Scope ---
        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope(); // Simulate a request scope

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        // --- Execute Logic ---
        await RunSampleLogic(sender, publisher);
    }

    public static async Task RunSample(ISender sender, IPublisher publisher)
    {
        // See next section
    }
}
```

### Sending Requests & Publishing Notifications

Inject `ISender` and `IPublisher` into your services (controllers, etc.) and use them to dispatch messages.

```csharp
// Inside the RunSampleLogic method from above

public static async Task RunSample(ISender sender, IPublisher publisher)
{
    Console.WriteLine("--- Sending GetUserRequest ---");
    string userResult = await sender.Send(new Sample.GetUserRequest(123));
    Console.WriteLine($"--> Result: {userResult}\n");

    Console.WriteLine("--- Sending CreateOrderRequest ---");
    int orderResult = await sender.Send(new Sample.CreateOrderRequest("Gadget"));
    Console.WriteLine($"--> Result: {orderResult}\n");

    Console.WriteLine("--- Publishing UserLoggedInEvent ---");
    await publisher.Publish(new Sample.UserLoggedInEvent(123));
    Console.WriteLine("--- Notification Published ---\n");

    Console.WriteLine("--- Sending Request with Validation Failure ---");
    try
    {
        await sender.Send(new Sample.GetUserRequest(-1));
    }
    catch (FluentValidation.ValidationException ex)
    {
        Console.WriteLine($"--> Caught Expected ValidationException: {ex.Errors.FirstOrDefault()?.ErrorMessage}\n");
    }
}
```

### Notification Pipelines (Resilience & Retries)

Demonstrates how to wrap notification handlers with middleware using `INotificationPipelineBehavior<TNotification>`.
*   **Isolation:** Notification behaviors wrap *each individual handler execution*.
*   **Resilience (Swallowing Exceptions):** For non-critical handlers, catch and log exceptions from a specific handler so others continue running.
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

### Example Output

From `Sample.Program`:

```text
--- Sending GetUserRequest ---
Logging: Handling GetUserRequest
Audit: Processing request at 27/3/2025 3:53:07 pm
Audit: Completed request at 27/3/2025 3:53:07 pm
Logging: Handled GetUserRequest
--> Result: User 123 at 27/3/2025 3:53:07 pm

--- Sending CreateOrderRequest ---
Logging: Handling CreateOrderRequest
Transaction: Starting with ID 0a12d204-8547-41e8-b6ca-d89098081ab6
Transaction: Completed with ID 0a12d204-8547-41e8-b6ca-d89098081ab6
Logging: Handled CreateOrderRequest
--> Result: 42

--- Publishing UserLoggedInEvent ---
Logged: User 123 logged in.
Analytics: User 123 tracked.
--- Notification Published ---

--- Sending GetUserRequest with Validation Failure ---
Logging: Handling GetUserRequest
--> Caught Expected ValidationException: UserId must be positive

--- Sending CreateOrderRequest with Validation Failure ---
Logging: Handling CreateOrderRequest
--> Caught Expected ValidationException: Product cannot be empty
```

---

## License

SwitchMediator is licensed under the [MIT License](LICENSE).