using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ValueTaskBasic;

[SwitchMediator]
public partial class TestMediator;

[RequestHandler(typeof(PingHandler))]
public class Ping : IRequest<string>;
public class PingHandler : IValueRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken = default) => ValueTask.FromResult("Pong");
}
