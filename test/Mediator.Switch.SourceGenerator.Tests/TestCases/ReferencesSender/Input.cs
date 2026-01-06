using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ReferencesSender;

[SwitchMediator]
public partial class TestMediator;

[RequestHandler(typeof(PingHandler))]
public class Ping : IRequest<string>;
public class PingHandler : IRequestHandler<Ping, string>
{
    private readonly ISender _sender;

    public PingHandler(ISender sender) => _sender = sender;

    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}