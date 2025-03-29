using Mediator.Switch;
using System.Threading.Tasks;

namespace Test.BasicPipeline;

public class Ping : IRequest<string>;
 
public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request) => Task.FromResult("Pong");
}

public class GenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next)
    {
        return await next();
    }
}
