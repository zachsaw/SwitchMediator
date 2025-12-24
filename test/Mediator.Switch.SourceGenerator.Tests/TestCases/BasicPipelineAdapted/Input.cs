using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.BasicPipelineAdapted;

public interface IResult<out T>
{
    T Value { get; }
}

public class Result<T> : IResult<T>
{
    public T Value { get; set; }
    public Result(T value)
    {
        Value = value;
    }
}

public interface IVersionedResponse
{
    int Version { get; }
}

public class VersionedResponse : IVersionedResponse
{
    public int Version { get; set; }
}

public class Ping : IRequest<Result<VersionedResponse>>;

public class PingHandler : IRequestHandler<Ping, Result<VersionedResponse>>
{
    public Task<Result<VersionedResponse>> Handle(Ping request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Result<VersionedResponse>(new VersionedResponse { Version = 42 }));
}

public class Pong : IRequest<VersionedResponse>;

public class PongHandler : IRequestHandler<Pong, VersionedResponse>
{
    public Task<VersionedResponse> Handle(Pong request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VersionedResponse {Version = 42});
}

public class GenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, Result<TResponse>>
    where TRequest : notnull
    where TResponse : IVersionedResponse
{
    public async Task<Result<TResponse>> Handle(TRequest request, RequestHandlerDelegate<Result<TResponse>> next, CancellationToken cancellationToken = default)
    {
        return await next(cancellationToken);
    }
}