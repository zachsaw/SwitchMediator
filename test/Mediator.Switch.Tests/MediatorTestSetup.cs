using FluentValidation;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

[SwitchMediator]
public partial class SwitchMediator;

public static class MediatorTestSetup
{
    public static (
        ServiceProvider ServiceProvider,
        IServiceScope Scope,
        ISender Sender,
        IPublisher Publisher,
        NotificationTracker Tracker)
        Setup(
            Action<SwitchMediatorOptions>? configureMediator = null,
            Action<IServiceCollection>? configureServices = null, // Action to add test-specific services
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        var services = new ServiceCollection();

        // --- Register COMMON services needed by most tests ---
        services.AddValidatorsFromAssembly(typeof(MediatorTestSetup).Assembly, includeInternalTypes: true);
        services.AddSingleton<NotificationTracker>(); // Tracker is usually singleton

        // --- Allow test classes to add their SPECIFIC services ---
        configureServices?.Invoke(services);

        // --- Register SwitchMediator ---
        services.AddMediator<SwitchMediator>(op =>
        {
            op.KnownTypes = SwitchMediator.KnownTypes; // Pass KnownTypes if required
            op.ServiceLifetime = lifetime;

            // Apply specific mediator configuration passed in by the test method/class
            configureMediator?.Invoke(op);
        });

        // --- Build Provider and Scope ---
        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateScope(); // Create scope immediately

        // --- Resolve core Mediator services FROM THE SCOPE ---
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var tracker = scope.ServiceProvider.GetRequiredService<NotificationTracker>(); // Tracker resolved from scope too

        return (serviceProvider, scope, sender, publisher, tracker);
    }
}