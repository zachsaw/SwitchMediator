using FluentValidation;
using Mediator.Switch;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.ConsoleApp;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection()
            .AddValidatorsFromAssembly(typeof(Program).Assembly)

            // Register SwitchMediator
            .AddMediator<SwitchMediator>(op =>
            {
                op.KnownTypes = SwitchMediator.KnownTypes;
                op.ServiceLifetime = ServiceLifetime.Singleton;

                // Ordering for UserLoggedInEvent
                op.OrderNotificationHandlers<UserLoggedInEvent>(
                    typeof(UserLoggedInLogger)
                );

                // Ordering for SystemAlert (Resilience Demo)
                op.OrderNotificationHandlers<SystemAlert>(
                    typeof(FailingSystemAlertHandler),
                    typeof(LoggingSystemAlertHandler)
                );
            });

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        await RunSample(sender, publisher);
    }

    private static async Task RunSample(ISender sender, IPublisher publisher)
    {
        Console.WriteLine("--- Sending GetUserRequest ---");
        var userRequest = new GetUserRequest(123);
        var userResult = await sender.Send(userRequest);
        Console.WriteLine($"--> Result: {userResult}\n");

        Console.WriteLine("--- Sending CreateOrderRequest ---");
        var orderRequest = new CreateOrderRequest("Book");
        var orderResult = await sender.Send(orderRequest);
        Console.WriteLine($"--> Result: {orderResult}\n");

        Console.WriteLine("--- Sending Cat ---");
        var catRequest = new Cat();
        await sender.Send(catRequest);
        Console.WriteLine("--> Done\n");

        Console.WriteLine("--- Sending Dog ---");
        var dogRequest = new Dog();
        await sender.Send(dogRequest);
        Console.WriteLine("--> Done\n");

        Console.WriteLine("--- Publishing UserLoggedInEvent ---");
        var loginEvent = new DerivedUserLoggedInEvent(123);
        await publisher.Publish(loginEvent);
        Console.WriteLine("--- Notification Published ---\n");

        Console.WriteLine("--- Publishing SystemAlert (Resilience Demo) ---");
        var alert = new SystemAlert("Database Latency High");
        await publisher.Publish(alert);
        Console.WriteLine("--- SystemAlert Published ---\n");

        Console.WriteLine("--- Publishing UnstableServiceEvent (Retry Demo) ---");
        // This demonstrates the Retry Notification Behavior.
        // The handler is programmed to fail twice. The behavior will retry it until it succeeds on the 3rd try.
        var retryEvent = new UnstableServiceEvent("JOB-9000");
        await publisher.Publish(retryEvent);
        Console.WriteLine("--- UnstableServiceEvent Published ---\n");

        Console.WriteLine("--- Sending GetUserRequest with Validation Failure ---");
        try
        {
            await sender.Send(new GetUserRequest(-1));
        }
        catch (ValidationException ex)
        {
            Console.WriteLine($"--> Caught Expected ValidationException: {ex.Errors.FirstOrDefault()?.ErrorMessage}\n");
        }

        Console.WriteLine("--- Sending CreateOrderRequest with Validation Failure ---");
        try
        {
            await sender.Send(new CreateOrderRequest(""));
        }
        catch (ValidationException ex)
        {
            Console.WriteLine($"--> Caught Expected ValidationException: {ex.Errors.FirstOrDefault()?.ErrorMessage}\n");
        }
    }
}