using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.HandlerCacheDictionary;

[SwitchMediator(HandlerCache = HandlerCache.Dictionary)]
public partial class TestMediator;

[RequestHandler(typeof(CacheDictionaryPingHandler))]
public class CacheDictionaryPing : IRequest<string>;

public class CacheDictionaryPingHandler : IRequestHandler<CacheDictionaryPing, string>
{
    public Task<string> Handle(CacheDictionaryPing request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
