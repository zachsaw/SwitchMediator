using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ReferencesPublisher;

[SwitchMediator]
public partial class TestMediator;

[RequestHandler(typeof(PingHandler))]
public class Ping : IRequest<string>;
public class PingHandler : IRequestHandler<Ping, string>
{
    private readonly IPublisher _publisher;

    public PingHandler(IPublisher publisher) => _publisher = publisher;

    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}