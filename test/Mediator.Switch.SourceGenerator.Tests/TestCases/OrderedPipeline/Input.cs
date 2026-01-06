using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.OrderedPipeline;

[SwitchMediator]
public partial class TestMediator;

// The Request
public class CalculationRequest : IRequest<int>
{
    public int Value { get; set; }
}

// The Handler
public class CalculationRequestHandler : IRequestHandler<CalculationRequest, int>
{
    public Task<int> Handle(CalculationRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("--> Handler executing");
        return Task.FromResult(request.Value * request.Value);
    }
}

// Behavior with Order 2
[PipelineBehaviorOrder(2)]
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(" -> Behavior Order 2 (Validation) Start");
        // Simulate validation
        await Task.Delay(5); // Small delay
        var response = await next(cancellationToken);
        Console.WriteLine(" <- Behavior Order 2 (Validation) End");
        return response;
    }
}

// Behavior with Order 1
[PipelineBehaviorOrder(1)]
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("-> Behavior Order 1 (Logging) Start");
        var response = await next(cancellationToken);
        Console.WriteLine("<- Behavior Order 1 (Logging) End");
        return response;
    }
}

// Behavior with NO explicit order (should run after ordered ones by default)
public class MonitoringBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("  -> Behavior Order Default (Monitoring) Start");
        var response = await next(cancellationToken);
        Console.WriteLine("  <- Behavior Order Default (Monitoring) End");
        return response;
    }
}