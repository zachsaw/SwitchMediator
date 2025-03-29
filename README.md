# SwitchMediator

[![Build Status](https://img.shields.io/github/actions/workflow/status/zachsaw/SwitchMediator/dotnet.yml?branch=main)](https://github.com/zachsaw/SwitchMediator/actions)
[![NuGet Version](https://img.shields.io/nuget/v/Mediator.Switch.svg)](https://www.nuget.org/packages/Mediator.Switch/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SwitchMediator: A Blazing Fast, Source-Generated Mediator for .NET**

SwitchMediator provides a high-performance implementation of the mediator pattern, offering an API surface familiar to users of popular libraries like [MediatR](https://github.com/jbogard/MediatR). By leveraging **C# Source Generators**, SwitchMediator eliminates runtime reflection for handler discovery and dispatch, instead generating highly optimized `switch` statements at compile time. We also want you to <em>**Switch**</em> your <em>**Mediator**</em> to ours, get it? 😉

Aside from performance, SwitchMediator is first and foremost designed to overcome frequent community frustrations with MediatR, addressing factors that have hindered its wider adoption.

**The result? Faster execution, improved startup times, step-into debuggability, and compile-time safety.**

---

## Table of Contents

*   [Why SwitchMediator?](#why-switchmediator)
*   [Key Advantages Over Reflection-Based Mediators](#key-advantages-over-reflection-based-mediators)
*   [Features](#features)
*   [Installation](#installation)
*   [Usage Example](#usage-example)
    *   [DI Setup](#di-setup)
    *   [Sending Requests & Publishing Notifications](#sending-requests--publishing-notifications)
    *   [Example Output](#example-output)
*   [License](#license)

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
*   ⏱️ **Faster Startup:** Less work (like assembly scanning for handlers) needs to happen when your application boots up.
*   ✂️ **Trimming / AOT Friendly:** Avoids runtime reflection, making the dispatch mechanism inherently more compatible with .NET trimming and AOT compilation. (Note: Ensure handlers and dependencies are also trimming/AOT safe).
*   🔍 **Explicitness:** The generated code serves as clear, inspectable documentation of how requests are routed and pipelines are constructed for each message type.

## Features

*   Request/Response messages (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`)
*   Notification messages (`INotification`, `INotificationHandler<TNotification>`)
*   Pipeline Behaviors (`IPipelineBehavior<TRequest, TResponse>`) for cross-cutting concerns.
*   Native support for Result pattern (e.g. [FluentResults](https://github.com/altmann/FluentResults)).
*   Flexible Pipeline Behavior Ordering via `[PipelineBehaviorOrder(int order)]`.
*   Pipeline Behavior Constraints using standard C# generic constraints (`where TRequest : ...`).
*   Explicit Notification Handler Ordering via DI configuration.
*   Seamless integration with `Microsoft.Extensions.DependencyInjection`.

## Installation

You'll typically need two packages:

1.  **`Mediator.Switch`:** Contains the core interfaces (`IRequest`, `INotification`, `IPipelineBehavior`, etc.) and the source generator itself.
2.  **`Mediator.Switch.Extensions.Microsoft.DependencyInjection`:** Provides extension methods for easy registration with the standard .NET DI container.

```bash
dotnet add package Mediator.Switch
dotnet add package Mediator.Switch.Extensions.Microsoft.DependencyInjection
```

## Usage Example

This example assumes you have defined your `IRequest`, `INotification`, `IRequestHandler`, `INotificationHandler`, and `IPipelineBehavior` types in your project (e.g., in the `Sample` namespace and `Program`'s assembly).

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
        // 2. Pass assembly(s) containing handlers, messages, behaviors for scanning.
        services.AddScoped<SwitchMediator>(typeof(Program).Assembly)
            // 3. Optionally, specify notification handler order.
            .OrderNotificationHandlers<Sample.UserLoggedInEvent>(
                typeof(Sample.UserLoggedInLogger),
                typeof(Sample.UserLoggedInAnalytics)
            );

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
