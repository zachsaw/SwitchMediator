using Mediator.Switch;
using System.Threading.Tasks;

namespace Test.Basic;

public class Ping : IRequest<string>;
public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request) => Task.FromResult("Pong");
}
