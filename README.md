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

*   [What's New in V3](#whats-new-in-v3)
*   [What's New in V2](#whats-new-in-v2)
*   [Why SwitchMediator?](#why-switchmediator)
*   [Key Advantages Over Reflection-Based Mediators](#key-advantages-over-reflection-based-mediators)
*   [Features](#features)
*   [Installation](#installation)
*   [Usage Example](#usage-example)
    *   [1. Define the Mediator](#1-define-the-mediator)
    *   [2. DI Setup](#2-di-setup)
    *   [3. Sending Requests & Publishing Notifications](#3-sending-requests--publishing-notifications)
*   [License](#license)

---

## What's New in V3

### ⚠️ Breaking Change: User-Defined Partial Class
In previous versions, the library automatically generated a class named `SwitchMediator`. In V3, **you must define the mediator class yourself** as a `partial class` and mark it with the `[SwitchMediator]` attribute.

**Why?**
*   **Namespace Control:** You can now place the mediator in any namespace you choose.
*   **Visibility Control:** You decide if your mediator is `public` or `internal`.

### ⚠️ Breaking Change: `KnownTypes` Location
The `KnownTypes` property is no longer on a static `SwitchMediator` class. It is now generated as a static property on **your** custom partial class.

---

## What's New in V2

### 1. Notification Pipeline Behaviors (`INotificationPipelineBehavior`)
V2 introduced support for pipeline behaviors on Notifications.

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

**After (V2+):**
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
*   🧐 **Enhanced Debuggability:** You can directly **step into the generated code**! See the exact `switch` statement matching your request, observe the explicit nesting of pipeline behavior calls (`await next(...)`), and step directly into your handler code. This provides unparalleled transparency compared to debugging reflection-based dispatch logic.
*   ✅ **Compile-Time Safety:** Handler discovery happens during the build. Missing request handlers result in **build errors**, not runtime exceptions, catching issues earlier in the development cycle.
*   ✂️ **Trimming / AOT Friendly:** Avoids runtime reflection, making the dispatch mechanism inherently more compatible with .NET trimming and AOT compilation.
*   🔍 **Explicitness:** The generated code serves as clear, inspectable documentation of how requests are routed and pipelines are constructed for each message type.

## Features

*   Request/Response messages (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`)
*   Notification messages (`INotification`, `INotificationHandler<TNotification>`)
*   Pipeline Behaviors (`IPipelineBehavior<TRequest, TResponse>`) for cross-cutting concerns.
*   Notification Pipeline Behaviors (`INotificationPipelineBehavior<TNotification>`) for per-handler middleware (Resilience, Retries, etc.).
*   Native support for Result pattern (e.g. [FluentResults](https://github.com/altmann/FluentResults)).
*   Flexible Pipeline Behavior Ordering via `[PipelineBehaviorOrder(int order)]`.
*   Explicit Notification Handler Ordering via DI configuration.
*   Seamless integration with `Microsoft.Extensions.DependencyInjection`.

## Installation

You'll typically need two packages:

1. **`Mediator.Switch.SourceGenerator`:** The SwitchMediator source generator itself.
2. **`Mediator.Switch.Extensions.Microsoft.DependencyInjection`:** (Optional) Provides extension methods for easy registration with the standard .NET DI container.

```bash
dotnet add package Mediator.Switch.SourceGenerator
dotnet add package Mediator.Switch.Extensions.Microsoft.DependencyInjection
```

## Usage Example

Refer to [Sample app](sample/Sample.ConsoleApp) for more information.

### 1. Define the Mediator

Create a `partial class` in your project and mark it with `[SwitchMediator]`. This tells the source generator where to generate the dispatch logic.

> **Note:** Because this class is instantiated via Dependency Injection (Reflection), your IDE might warn that the class is unused. You can suppress this warning as shown below.

```csharp
using Mediator.Switch;
using System.Diagnostics.CodeAnalysis;

namespace My.Application;

[SwitchMediator]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via DI")]
public partial class AppMediator; 
```

### 2. DI Setup

Register your custom mediator class in your application's composition root (e.g., `Program.cs`).

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mediator.Switch;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using My.Application; // Namespace where you defined AppMediator

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();

        // --- SwitchMediator Registration ---
        // Register your custom partial class.
        // The extension method automatically finds the generated 'KnownTypes' on AppMediator.
        services.AddMediator<AppMediator>(op =>
        {
            op.ServiceLifetime = ServiceLifetime.Scoped;
            
            // Optional: Specify notification handler order
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(UserLoggedInLogger),
                typeof(UserLoggedInAnalytics)
            );
        });

        // --- Build and Scope ---
        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        await RunSampleLogic(sender, publisher);
    }
}
```

### 3. Sending Requests & Publishing Notifications

Inject `ISender` and `IPublisher` into your services (controllers, etc.) and use them to dispatch messages.

```csharp
public static async Task RunSampleLogic(ISender sender, IPublisher publisher)
{
    // Send a Request
    var response = await sender.Send(new GetUserRequest(123));
    
    // Publish a Notification
    await publisher.Publish(new UserLoggedInEvent(123));
}
```

---

## License

SwitchMediator is licensed under the [MIT License](LICENSE).