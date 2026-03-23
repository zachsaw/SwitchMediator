# SwitchMediator

[![Build Status](https://img.shields.io/github/actions/workflow/status/zachsaw/SwitchMediator/dotnet.yml?branch=main)](https://github.com/zachsaw/SwitchMediator/actions)
[![NuGet Version](https://img.shields.io/nuget/v/Mediator.Switch.svg)](https://www.nuget.org/packages/Mediator.Switch/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SwitchMediator: A High-Performance, Source-Generated Mediator for .NET**

SwitchMediator is a zero-allocation, AOT-friendly implementation of the mediator pattern, designed to be API-compatible with popular libraries like [MediatR](https://github.com/jbogard/MediatR).

By leveraging **C# Source Generators**, SwitchMediator moves the heavy lifting from runtime to compile time. Instead of scanning assemblies and using Reflection for every dispatch, it generates a static, type-safe lookup (using `FrozenDictionary` on .NET 8+) that routes messages to handlers instantly.

**The result is a mediator that offers:**
* **Zero Runtime Reflection:** No scanning cost at startup.
* **AOT & Trimming Compatibility:** Native support for modern .NET deployment models.
* **Compile-Time Safety:** Missing handlers are caught during the build, not at runtime.
* **Step-Through Debugging:** You can step directly into the generated dispatch code to see exactly how your pipeline works.

[(See Benchmark Results)](benchmark/Mediator.Switch.Benchmark/benchmark_results.md)
 
---

## Table of Contents

* [What's New in V3.1](#whats-new-in-v31)
* [What's New in V3](#whats-new-in-v3)
* [What's New in V3.2](#whats-new-in-v32)
* [What's New in V3.1](#whats-new-in-v31)
* [What's New in V3](#whats-new-in-v3)
* [What's New in V2](#whats-new-in-v2)
* [Why SwitchMediator?](#why-switchmediator)
* [🌟 Feature Spotlight: True Polymorphic Dispatch](#-feature-spotlight-true-polymorphic-dispatch)
* [Key Advantages](#key-advantages)
* [Features](#features)
* [Installation](#installation)
* [Usage Example](#usage-example)
* [License](#license)

 ---

## What's New in V3.2

### Per-Handler Typed Pipeline Injection

V3.2 adds first-class support for directly injecting a **fully-pipelined `IRequestHandler<TRequest,TResponse>`** (or `IValueRequestHandler<TRequest,TResponse>`) from DI, without going through `ISender` or `IValueSender`. This is useful when you need to call a specific handler inline — for example inside another handler or service — while still getting all pipeline behaviors applied.

#### Opting In

Pass `YourMediator.PipelinedHandlerTypes` to `SwitchMediatorOptions.PipelinedHandlerTypes` during DI setup:

```csharp
services.AddMediator<AppMediator>(op =>
{
    op.KnownTypes = AppMediator.KnownTypes;
    op.ServiceLifetime = ServiceLifetime.Scoped;
    // Opt in: expose IRequestHandler<T,R> / IValueRequestHandler<T,R> from DI
    op.PipelinedHandlerTypes = AppMediator.PipelinedHandlerTypes;
});
```

Once registered, you can inject handlers directly:

```csharp
public class MyService(IRequestHandler<GetUserRequest, User> handler)
{
    public Task<User> GetUser(int id) => handler.Handle(new GetUserRequest(id), default);
}
```

Or the ValueTask variant:

```csharp
public class MyService(IValueRequestHandler<GetUserRequest, User> handler)
{
    public ValueTask<User> GetUser(int id) => handler.Handle(new GetUserRequest(id), default);
}
```

Both `IRequestHandler<TRequest,TResponse>` and `IValueRequestHandler<TRequest,TResponse>` resolve to the same generated wrapper object within a single DI scope.

#### How It Works

The source generator emits a **sealed inner class** `PipelinedHandler_<RequestName>` inside your mediator partial class. The wrapper holds a reference to the mediator and delegates directly to the private `Handle_XXX` method — the same method called by `ISender.Send`. This guarantees the full behavior chain is applied.

```csharp
// Generated (simplified):
public sealed class PipelinedHandler_GetUserRequest :
        IRequestHandler<GetUserRequest, User>,
        IValueRequestHandler<GetUserRequest, User>
{
    private readonly AppMediator _mediator;
    public PipelinedHandler_GetUserRequest(AppMediator mediator) => _mediator = mediator;

    Task<User> IRequestHandler<GetUserRequest, User>.Handle(GetUserRequest req, CancellationToken ct)
        => _mediator.Handle_GetUserRequest(req, ct); // runs full pipeline

    ValueTask<User> IValueRequestHandler<GetUserRequest, User>.Handle(GetUserRequest req, CancellationToken ct)
        => new(_mediator.Handle_GetUserRequest(req, ct));
}
```

#### Known Constraints and Incompatibilities

| Constraint | Details |
| :--- | :--- |
| **Opt-in only** | The feature is disabled by default. You must set `op.PipelinedHandlerTypes = YourMediator.PipelinedHandlerTypes` to enable it. |
| **Task ↔ ValueTask adaptation** | `IRequestHandler<T,R>` always returns `Task<TResponse>`. For ValueTask-based handlers the result is wrapped via `.AsTask()`. Similarly, `IValueRequestHandler<T,R>` always returns `ValueTask<TResponse>`, wrapping Task-based results in `new ValueTask<T>(...)`. |
| **Pipeline type must match the handler** | If you have `IValueRequestHandler` handlers you must use `IValuePipelineBehavior` behaviors (not `IPipelineBehavior`). The SMD002 analyzer enforces this at compile time. This constraint is independent of whether you enable pipelined injection. |
| **No mixed pipelines** | You cannot have both `IPipelineBehavior` and `IValuePipelineBehavior` behaviors applicable to the same request type. This is an existing restriction enforced by SMD002. |
| **Constructor injects concrete mediator** | The generated wrapper takes the concrete mediator type (e.g., `AppMediator`) as its constructor argument. The DI registration casts the resolved `IMediator` to `AppMediator` using `Activator.CreateInstance`. This is safe as long as the concrete type is correctly registered. |
| **SMD002 analyzer update** | The `PipelineConsistencyAnalyzer` (SMD002) has been updated to skip types from auto-generated files (`.g.cs`). This prevents false-positive errors that previously fired when the generated wrapper classes (which implement both `IRequestHandler` and `IValueRequestHandler`) were analyzed alongside user-defined Task-based behaviors. |

 ---

## What's New in V3.1

### Hybrid `ValueTask` Support

V3.1 introduces `IValueMediator`, `IValueSender`, and `IValuePublisher` — a parallel set of interfaces that dispatch via `ValueTask` instead of `Task`.

The generated mediator class now implements **both** `IMediator` and `IValueMediator` simultaneously. You can inject whichever interface suits your performance needs.

**New interfaces added:**

| Interface | Purpose |
| :--- | :--- |
| `IValueSender` | `ValueTask<TResponse> Send(IRequest<TResponse>, CancellationToken)` |
| `IValuePublisher` | `ValueTask Publish(INotification, CancellationToken)` |
| `IValueMediator` | Combines `IValueSender` + `IValuePublisher` |
| `IValueRequestHandler<TRequest, TResponse>` | ValueTask-returning handler |
| `IValueNotificationHandler<TNotification>` | ValueTask-returning notification handler |
| `IValuePipelineBehavior<TRequest, TResponse>` | ValueTask-returning pipeline behavior |
| `IValueNotificationPipelineBehavior<TNotification>` | ValueTask-returning notification pipeline behavior |

**Allocation characteristics:**

| Path | Allocation |
| :--- | :--- |
| `IValuePublisher.Publish` + `IValueNotificationHandler` | **Zero** allocation in the dispatch layer |
| `IPublisher.Publish` + `INotificationHandler` (existing) | Already zero allocation (unchanged) |
| `IValueSender.Send` + `IValueRequestHandler` | **Zero** allocation — fully alloc-free in the mediator infrastructure |
| `ISender.Send` + `IRequestHandler` (existing) | ~96 B per call (unchanged) |

> **Note:** `IValueSender.Send` paired with `IValueRequestHandler` (and no pipeline behaviors) is fully alloc-free. `IValuePublisher.Publish` uses the same zero-cost dictionary routing, but the generated notification fan-out uses async iteration, which may allocate when your handlers are genuinely asynchronous (as per normal usage of .net `ValueTask`).

### New Compile-Time Analyzer: **SMD002**

A new `PipelineConsistencyAnalyzer` (diagnostic ID `SMD002`) reports a **build error** if you accidentally mix `IPipelineBehavior` (Task) behaviors with `IValueRequestHandler` (ValueTask) handlers, or vice versa.

**Why does this happen?** 

SwitchMediator automatically applies behaviors to handlers based on generic constraints. A behavior like `ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull` applies to **all** requests (since all requests satisfy `notnull`). If you also have a ValueTask handler for the same request type, the analyzer detects the mismatch and reports SMD002.

```csharp
// ❌ SMD002 error: Generic IPipelineBehavior applies to ValueTask handler
public class FastStatusCheckHandler : IValueRequestHandler<FastStatusCheckRequest, bool> { ... }

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull  // This applies to ALL requests — including FastStatusCheckRequest!
{ ... }

// ✅ Fix 1: Constrain the behavior so it only applies to specific requests
public interface IValidatable { }

public class FastStatusCheckRequest : IRequest<bool> { ... }  // No IValidatable — behavior doesn't apply
public class UserUpdateRequest : IRequest<bool>, IValidatable { ... }  // Has IValidatable — behavior applies

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IValidatable  // Now only applies to requests implementing IValidatable
{ ... }

// ✅ Fix 2: Create a matching ValueTask version for behaviors that should apply to both pipelines
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull { ... }  // For Task handlers

public class ValidationValueBehavior<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
    where TRequest : notnull { ... }  // For ValueTask handlers
```

**How the analyzer works:** The analyzer checks each behavior's generic constraints against handler types. A behavior only applies if the request type satisfies all of its constraints (e.g., `where TRequest : notnull, IValidatable`). This allows you to selectively apply behaviors to specific request types while avoiding cross-pipeline contamination.

### DI Registration

`AddMediator<T>()` automatically registers `IValueMediator`, `IValueSender`, and `IValuePublisher` alongside the existing `IMediator`, `ISender`, and `IPublisher`. No extra configuration needed.

```csharp
// All six interfaces are available after a single AddMediator call
var sender = sp.GetRequiredService<ISender>();           // Task-based
var valueSender = sp.GetRequiredService<IValueSender>(); // ValueTask-based (zero extra setup)
```

 ---

## What's New in V3

### ⚠️ Breaking Change: User-Defined Partial Class
In previous versions, the library automatically generated a class named `SwitchMediator`. In V3, **you must define the mediator class yourself** as a `partial class` and mark it with the `[SwitchMediator]` attribute.

**Why?**
* **Namespace Control:** You can now place the mediator in any namespace you choose.
* **Visibility Control:** You decide if your mediator is `public` or `internal`.

### ⚠️ Breaking Change: `KnownTypes` Location
The `KnownTypes` property is no longer on a static `SwitchMediator` class. It is now generated as a static property on **your** custom partial class.
 
---

## What's New in V2

### 1. Notification Pipeline Behaviors
V2 introduced support for pipeline behaviors on Notifications.

SwitchMediator wraps **each handler execution independently** in its own pipeline scope. This enables powerful patterns like **Resilience**: you can write a behavior that catches exceptions from a specific handler, logs them, and swallows them, ensuring that *other* handlers for the same notification still execute.

**The Performance Advantage:**
SwitchMediator generates this "Russian Doll" wrapping code at compile time. This makes the pipeline structure effectively "free" at runtime—zero allocation overhead for the pipeline construction itself.

### 2. ⚠️ Breaking Change: `next(cancellationToken)`
To further optimize performance, the signature for pipeline delegates has changed. You must now pass the `cancellationToken` explicitly to `next`.

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

Traditional mediator implementations often rely on runtime reflection to discover handlers. While flexible, this approach has trade-offs regarding startup time, memory allocation, and AOT compatibility.

**SwitchMediator solves this by handling discovery at build time.** The source generator creates explicit C# code with direct method calls, offering a "pay-as-you-go" approach where the cost is incurred during compilation, not during your application's execution.
 
---

## 🌟 Feature Spotlight: True Polymorphic Dispatch

Most source-generated mediators force you to have an **Exact Type Match** between your Request and your Handler.

**SwitchMediator is smarter.** It analyzes your type hierarchy at compile time and generates the necessary dispatch logic to support inheritance, **without** the runtime cost of walking the inheritance tree.

### The Feature Matrix

| Feature | **SwitchMediator** | **MediatR** (Reflection) | **Mediator** (Source Gen) |
 | :--- |:---------------------------:| :---: | :---: |
| **Method** | Source Generator | Runtime Reflection | Source Generator |
| **Request Inheritance** | ✅ **Supported** | ✅ Supported | ❌ **Not Supported** |
| **Notification Inheritance** | ✅ **Supported** (Fallback)* | ✅ Supported (Broadcast) | ❌ **Not Supported** |
| **AOT / Trimming** | ✅ **Native** | ⚠️ Difficult | ✅ Native |

* **Request Inheritance:** You can define a handler for `IRequestHandler<BaseClass, ...>` and send a `DerivedClass`. SwitchMediator detects this relationship during the build and routes `DerivedClass` directly to the base handler.
* **Notification Fallback (*):** If you publish a `UserCreatedEvent` (which inherits from `DomainEvent`), and you only have a handler for `DomainEvent`, SwitchMediator will automatically route it there. *(Note: Unlike MediatR which broadcasts to ALL handlers in the hierarchy, SwitchMediator targets the most specific handler found to avoid accidental double-execution).*

 ---

## Key Advantages

* 🚀 **Maximum Performance:** Eliminates runtime reflection lookup overhead. Ideal for performance-critical paths and high-throughput applications.
* 🧐 **Enhanced Debuggability:** You can directly **step into the generated code**! See the exact logic matching your request and observe the explicit nesting of pipeline behavior calls.
* ✅ **Compile-Time Safety:** Missing request handlers result in **build errors**, not runtime exceptions, catching issues earlier in the development cycle.
* ✂️ **Trimming / AOT Friendly:** Because there is no dynamic Reflection, the dispatch mechanism is inherently compatible with .NET trimming and NativeAOT.

## Features

* Request/Response messages (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`)
* **ValueTask Request/Response** (`IValueRequestHandler<TRequest, TResponse>`, `IValueSender`) for reduced-allocation dispatch
* Notification messages (`INotification`, `INotificationHandler<TNotification>`)
* **ValueTask Notifications** (`IValueNotificationHandler<TNotification>`, `IValuePublisher`) for zero-allocation notification dispatch
* **Hybrid `IValueMediator`**: generated class implements both `IMediator` and `IValueMediator` simultaneously
* **Polymorphic Dispatch** (Inheritance support for Requests and Notifications).
* Pipeline Behaviors (`IPipelineBehavior<TRequest, TResponse>`) for cross-cutting concerns.
* **ValueTask Pipeline Behaviors** (`IValuePipelineBehavior<TRequest, TResponse>`)
* Notification Pipeline Behaviors (`INotificationPipelineBehavior<TNotification>`) for per-handler middleware (Resilience, Retries, etc.).
* **SMD002** compile-time enforcement of Task/ValueTask pipeline consistency
* Native support for Result pattern (e.g. [FluentResults](https://github.com/altmann/FluentResults)).
* Flexible Pipeline Behavior Ordering via `[PipelineBehaviorOrder(int order)]`.
* Explicit Notification Handler Ordering via DI configuration.
* Seamless integration with `Microsoft.Extensions.DependencyInjection`.

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

         // Both Task-based and ValueTask-based interfaces are registered automatically
         var sender = scope.ServiceProvider.GetRequiredService<ISender>();
         var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
         var valueSender = scope.ServiceProvider.GetRequiredService<IValueSender>();     // New in V4
         var valuePublisher = scope.ServiceProvider.GetRequiredService<IValuePublisher>(); // New in V4

         await RunSampleLogic(sender, publisher, valueSender, valuePublisher);
     }
 }
 ```

### 3. Sending Requests & Publishing Notifications

Inject `ISender` and `IPublisher` (Task-based) or `IValueSender` and `IValuePublisher` (ValueTask-based) into your services and use them to dispatch messages.

 ```csharp
 public static async Task RunSampleLogic(ISender sender, IPublisher publisher,
     IValueSender valueSender, IValuePublisher valuePublisher)
 {
     // Task-based (backward compatible)
     var response = await sender.Send(new GetUserRequest(123));
     await publisher.Publish(new UserLoggedInEvent(123));

     // ValueTask-based (reduced-allocation path) — New in V4
     bool ok = await valueSender.Send(new FastStatusCheckRequest());
     await valuePublisher.Publish(new ServerStartedEvent());
 }
 ```
 
---

## License

SwitchMediator is licensed under the [MIT License](LICENSE).