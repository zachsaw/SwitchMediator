using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.ServiceProviderDirect;

[SwitchMediator(ServiceProvider = MediatorServiceProvider.Direct)]
public partial class TestMediator;

[RequestHandler(typeof(DirectPingHandler))]
public class DirectPing : IRequest<string>;

public class DirectPingHandler : IRequestHandler<DirectPing, string>
{
    public Task<string> Handle(DirectPing request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
