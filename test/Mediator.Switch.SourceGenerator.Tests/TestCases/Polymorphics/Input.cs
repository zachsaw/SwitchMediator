using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Polymorphics;

[SwitchMediator]
public partial class TestMediator;

// --- Polymorphic Requests ---

// Base Request
public abstract class AnimalRequest : IRequest<string>
{
    public string Name { get; set; } = "Unknown";
}

// Derived Request 1
public class DogRequest : AnimalRequest
{
    public bool IsGoodBoy { get; set; } = true;
}

// Derived Request 2
public class CatRequest : AnimalRequest
{
    public int LivesRemaining { get; set; } = 9;
}

// Another unrelated derived request (to ensure no accidental matching)
public abstract class VehicleRequest : IRequest<double> { }
public class CarRequest : VehicleRequest { public int Doors { get; set; } }
public class TruckRequest : VehicleRequest; // no handler - should fall back to VehicleRequestHandler


// Handlers
// Handler specifically for DogRequest
public class DogRequestHandler : IRequestHandler<DogRequest, string>
{
    public Task<string> Handle(DogRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{request.Name} says Woof! (Good boy status: {request.IsGoodBoy})");
    }
}

// Handler specifically for CatRequest
public class CatRequestHandler : IRequestHandler<CatRequest, string>
{
    public Task<string> Handle(CatRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{request.Name} says Meow! (Lives: {request.LivesRemaining})");
    }
}

public class GenericAnimalRequestHandler : IRequestHandler<AnimalRequest, string>
{
     public Task<string> Handle(AnimalRequest request, CancellationToken cancellationToken = default)
     {
         return Task.FromResult($"Generic handler: An animal named {request.Name}");
     }
}

// Handler for the unrelated CarRequest
public class CarRequestHandler : IRequestHandler<CarRequest, double>
{
    public Task<double> Handle(CarRequest request, CancellationToken cancellationToken = default) => Task.FromResult(42.0);
}

public class VehicleRequestHandler : IRequestHandler<VehicleRequest, double>
{
    public Task<double> Handle(VehicleRequest request, CancellationToken cancellationToken = default) => Task.FromResult(24.0);
}


// --- Polymorphic Notifications ---

// Base Notification
public abstract class DomainEvent : INotification
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

// Derived Notification 1
public class UserCreatedEvent : DomainEvent
{
    public int UserId { get; set; }
}

// Derived Notification 2
public class OrderPlacedEvent : DomainEvent
{
    public Guid OrderId { get; set; }
}


// Notification Handlers

// Handler specifically for UserCreatedEvent
public class UserCreatedEmailHandler : INotificationHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"EMAIL: Welcome user {notification.UserId} created at {notification.Timestamp}");
        return Task.CompletedTask;
    }
}

// Handler specifically for OrderPlacedEvent
public class OrderPlacedInventoryHandler : INotificationHandler<OrderPlacedEvent>
{
    public Task Handle(OrderPlacedEvent notification, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"INVENTORY: Adjusting stock for order {notification.OrderId} placed at {notification.Timestamp}");
        return Task.CompletedTask;
    }
}

public class GenericDomainEventHandler : INotificationHandler<DomainEvent>
{
    public Task Handle(DomainEvent notification, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"AUDIT: Domain event of type {notification.GetType().Name} occurred at {notification.Timestamp}");
        return Task.CompletedTask;
    }
}

// Handler also specifically for UserCreatedEvent (Multiple handlers for same notification)
public class UserCreatedAnalyticsHandler : INotificationHandler<UserCreatedEvent>
{
     public Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"ANALYTICS: Tracking creation for user {notification.UserId}");
        return Task.CompletedTask;
    }
}