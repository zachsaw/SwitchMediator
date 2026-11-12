using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

/// <summary>
/// Guards the registration of the ValueTask interfaces (<see cref="IValueMediator"/>,
/// <see cref="IValueSender"/>, <see cref="IValuePublisher"/>): they are aliases of the
/// mediator and must always resolve to the very same instance the scope already holds.
/// </summary>
public class ValueInterfaceRegistrationTests
{
    [Fact]
    public void ValueInterfaces_ResolveToTheSameInstanceAsIMediator()
    {
        var (serviceProvider, scope, _, _, _) = MediatorTestSetup.Setup();

        using (serviceProvider)
        using (scope)
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<IValueMediator>());
            Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<IValueSender>());
            Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<IValuePublisher>());
        }
    }

    [Fact]
    public void ValueSender_IsScopedPerResolutionScope()
    {
        var (serviceProvider, scope, _, _, _) = MediatorTestSetup.Setup();

        using (serviceProvider)
        using (scope)
        {
            var first = scope.ServiceProvider.GetRequiredService<IValueSender>();

            using var secondScope = serviceProvider.CreateScope();
            var second = secondScope.ServiceProvider.GetRequiredService<IValueSender>();

            Assert.NotSame(first, second);
        }
    }
}
