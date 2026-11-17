using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

/// <summary>
/// Resolves every component the mediator can dispatch, so a handler whose dependencies are not
/// registered fails when the application starts instead of on the first send of that request.
/// The container cannot do it itself: the components are registered through factories, which
/// ValidateOnBuild cannot inspect.
/// </summary>
internal sealed class MediatorGraphValidator(
    IReadOnlyList<(Type Service, Type Implementation)> components,
    IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var failures = new List<string>();

        foreach (var (service, implementation) in components)
        {
            try
            {
                scope.ServiceProvider.GetRequiredService(service);
            }
            catch (Exception exception)
            {
                failures.Add($"{implementation.FullName}: {exception.GetBaseException().Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} mediator component(s) cannot be resolved. The same error would be thrown on the first send of those requests. Unresolvable components:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", failures)}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
