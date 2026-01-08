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
* Notification messages (`INotification`, `INotificationHandler<TNotification>`)
* **Polymorphic Dispatch** (Inheritance support for Requests and Notifications).
* Pipeline Behaviors (`IPipelineBehavior<TRequest, TResponse>`) for cross-cutting concerns.
* Notification Pipeline Behaviors (`INotificationPipelineBehavior<TNotification>`) for per-handler middleware (Resilience, Retries, etc.).
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