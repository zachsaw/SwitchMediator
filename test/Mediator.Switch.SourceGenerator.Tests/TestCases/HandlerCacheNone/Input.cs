using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.HandlerCacheNone;

[SwitchMediator(HandlerCache = HandlerCache.None)]
public partial class TestMediator;

[RequestHandler(typeof(CacheNonePingHandler))]
public class CacheNonePing : IRequest<string>;

public class CacheNonePingHandler : IRequestHandler<CacheNonePing, string>
{
    public Task<string> Handle(CacheNonePing request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
