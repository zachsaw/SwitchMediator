using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.PipelinedHandlerInjection;

[SwitchMediator]
public partial class TestMediator;

// Task-based request/handler with a Task pipeline behavior
public class Ping : IRequest<string>;

public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}

// ValueTask-based request/handler with a ValueTask pipeline behavior
[RequestHandler(typeof(PongHandler))]
public class Pong : IRequest<string>;

public class PongHandler : IValueRequestHandler<Pong, string>
{
    public ValueTask<string> Handle(Pong request, CancellationToken cancellationToken = default) => ValueTask.FromResult("Ping");
}

// Task pipeline behavior — applies only to Ping (Task-based handler)
public class PingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Ping
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        return await next(cancellationToken);
    }
}

// ValueTask pipeline behavior — applies only to Pong (ValueTask-based handler)
public class PongBehavior<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
    where TRequest : Pong
{
    public async ValueTask<TResponse> Handle(TRequest request, ValueRequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        return await next(cancellationToken);
    }
}
