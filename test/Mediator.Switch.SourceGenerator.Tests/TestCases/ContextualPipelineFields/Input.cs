using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ContextualPipelineFields;

[SwitchMediator(HandlerCache = HandlerCache.Fields)]
public partial class TestMediator;

public class ContextualPing : IRequest<string>;

public class ContextualPingHandler : IValueRequestHandler<ContextualPing, string>
{
    public ValueTask<string> Handle(ContextualPing request, CancellationToken cancellationToken = default) => new("Pong");
}

[PipelineBehaviorOrder(1)]
public class ContextualLogging<TRequest, TResponse> : IValueContextualPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public ValueTask<TResponse> Handle(TRequest request, ValueContextualRequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        => next(request, cancellationToken);
}

[PipelineBehaviorOrder(2)]
public class ContextualAudit<TRequest, TResponse> : IValueContextualPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public ValueTask<TResponse> Handle(TRequest request, ValueContextualRequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        => next(request, cancellationToken);
}
