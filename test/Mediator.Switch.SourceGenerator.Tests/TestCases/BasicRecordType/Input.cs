using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.BasicRecordType;

[SwitchMediator]
public partial class TestMediator;

[RequestHandler(typeof(PingHandler))]
public record Ping : IRequest<string>;
public record PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}