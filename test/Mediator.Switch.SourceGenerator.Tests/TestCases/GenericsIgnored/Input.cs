using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.GenericsIgnored;

[SwitchMediator]
public partial class TestMediator;

public class Ping : IRequest<string>;
public abstract class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}