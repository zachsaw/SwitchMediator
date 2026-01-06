using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ConstrainedPipeline;

[SwitchMediator]
public partial class TestMediator;

// Marker interface for constraint
public interface ISpecialProcessingRequired { }

// Request implementing the marker
public class SpecialProcessRequest : IRequest<Guid>, ISpecialProcessingRequired
{
    public string Data { get; set; } = "";
}

// Handler for the request
public class SpecialProcessRequestHandler : IRequestHandler<SpecialProcessRequest, Guid>
{
    public Task<Guid> Handle(SpecialProcessRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
}

// A generic behavior that applies to all requests
public class GenericLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Generic Log: Start {typeof(TRequest).Name}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Generic Log: End {typeof(TRequest).Name}");
        return response;
    }
}

// A behavior constrained to the marker interface
public class SpecialProcessingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ISpecialProcessingRequired // Constraint
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Special Behavior: Applying special processing to {typeof(TRequest).Name}");
        // Add some "processing" delay or modification if needed for testing runtime
        var response = await next(cancellationToken);
        Console.WriteLine("Special Behavior: Finished special processing");
        return response;
    }
}

// Another request that *doesn't* implement the marker
public class NormalRequest : IRequest<int>;
public class NormalRequestHandler : IRequestHandler<NormalRequest, int>
{
    public Task<int> Handle(NormalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(100);
}