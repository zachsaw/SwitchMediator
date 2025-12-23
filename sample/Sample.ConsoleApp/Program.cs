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
                op.OrderNotificationHandlers<UserLoggedInEvent>(
                    typeof(UserLoggedInLogger)
                    // any other types not specified above is assumed to have lower priority
                    // e.g., typeof(UserLoggedInAnalytics)
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