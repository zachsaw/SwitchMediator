using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.MixedPipeline;

[SwitchMediator]
public partial class TestMediator;

public class MixedPing : IRequest<string>;

public class MixedPingHandler : IValueRequestHandler<MixedPing, string>
{
    public ValueTask<string> Handle(MixedPing request, CancellationToken cancellationToken = default) => new("Pong");
}

[PipelineBehaviorOrder(1)]
public class MixedContextualLogging<TRequest, TResponse> : IValueContextualPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public ValueTask<TResponse> Handle(TRequest request, ValueContextualRequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        => next(request, cancellationToken);
}

[PipelineBehaviorOrder(2)]
public class MixedClassicAudit<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public ValueTask<TResponse> Handle(TRequest request, ValueRequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
        => next(cancellationToken);
}
