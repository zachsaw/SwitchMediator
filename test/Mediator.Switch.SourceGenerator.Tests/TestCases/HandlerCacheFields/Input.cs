using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.HandlerCacheFields;

[SwitchMediator(HandlerCache = HandlerCache.Fields)]
public partial class TestMediator;

[RequestHandler(typeof(CacheFieldsPingHandler))]
public class CacheFieldsPing : IRequest<string>;

public class CacheFieldsPingHandler : IRequestHandler<CacheFieldsPing, string>
{
    public Task<string> Handle(CacheFieldsPing request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
