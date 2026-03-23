using FluentResults;
using FluentValidation;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

/// <summary>
/// Verifies that IRequestHandler&lt;TRequest,TResponse&gt; and
/// IValueRequestHandler&lt;TRequest,TResponse&gt; can be injected from DI
/// and that calling Handle() runs the full pipeline (all applicable behaviors).
/// </summary>
public class PipelinedHandlerInjectionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;

    public PipelinedHandlerInjectionTests()
    {
        var services = new ServiceCollection();

        services.AddValidatorsFromAssembly(typeof(MediatorTestSetup).Assembly, includeInternalTypes: true);
        services.AddSingleton<NotificationTracker>();

        services.AddMediator<SwitchMediator>(op =>
        {
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Scoped;
            // Opt in: expose IRequestHandler<T,R> and IValueRequestHandler<T,R> from DI
            op.PipelinedHandlerTypes = SwitchMediator.PipelinedHandlerTypes;
        });

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    // ---------- IRequestHandler<TRequest, TResponse> (Task-based injection) ----------

    [Fact]
    public async Task TaskHandler_Handle_RunsFullPipeline_ValidRequest()
    {
        var handler = _scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, Result<User>>>();
        var request = new GetUserRequest(42);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.UserId);
        // VersionIncrementingBehavior increments Version from 50 to 51
        Assert.Equal(51, result.Value.Version);
    }

    [Fact]
    public async Task TaskHandler_Handle_RunsValidationBehavior_InvalidRequest()
    {
        var handler = _scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, Result<User>>>();
        var request = new GetUserRequest(-1); // Invalid: UserId must be positive

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(request, CancellationToken.None));
        Assert.Single(ex.Errors);
        Assert.Equal("UserId must be positive", ex.Errors.First().ErrorMessage);
    }

    [Fact]
    public async Task TaskHandler_Handle_SameInstanceInjectedMultipleTimes_UsesSameScope()
    {
        // Resolve twice from the same scope — must get the same instance (scoped lifetime)
        var handler1 = _scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, Result<User>>>();
        var handler2 = _scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, Result<User>>>();

        Assert.Same(handler1, handler2);
    }

    // ---------- IValueRequestHandler<TRequest, TResponse> (ValueTask-based injection) ----------

    [Fact]
    public async Task ValueTaskHandler_Handle_RunsFullPipeline_ValidRequest()
    {
        var handler = _scope.ServiceProvider.GetRequiredService<IValueRequestHandler<GetUserRequest, Result<User>>>();
        var request = new GetUserRequest(99);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value.UserId);
        Assert.Equal(51, result.Value.Version);
    }

    [Fact]
    public async Task ValueTaskHandler_Handle_RunsValidationBehavior_InvalidRequest()
    {
        var handler = _scope.ServiceProvider.GetRequiredService<IValueRequestHandler<GetUserRequest, Result<User>>>();
        var request = new GetUserRequest(0); // Invalid

        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await handler.Handle(request, CancellationToken.None));
        Assert.Single(ex.Errors);
        Assert.Equal("UserId must be positive", ex.Errors.First().ErrorMessage);
    }

    [Fact]
    public async Task TaskHandler_AndValueTaskHandler_ResolveToSameWrapper()
    {
        // Both IRequestHandler<T,R> and IValueRequestHandler<T,R> resolve to the same
        // underlying PipelinedHandler_XXX instance within a single scope
        var taskHandler = _scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, Result<User>>>();
        var valueHandler = _scope.ServiceProvider.GetRequiredService<IValueRequestHandler<GetUserRequest, Result<User>>>();

        // They are the same object (both resolve to the scoped PipelinedHandler_XXX)
        Assert.Same(taskHandler, valueHandler);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
