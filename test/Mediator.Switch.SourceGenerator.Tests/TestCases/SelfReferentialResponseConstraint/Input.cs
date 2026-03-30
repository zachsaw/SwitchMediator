using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.SelfReferentialResponseConstraint;

[SwitchMediator]
public partial class TestMediator;

public interface IErrorResultFactory<TSelf> where TSelf : IErrorResultFactory<TSelf>
{
    static abstract TSelf CreateFromError(string error);
}

public readonly struct OneOfResult : IErrorResultFactory<OneOfResult>
{
    public bool IsError { get; }
    public static OneOfResult CreateFromError(string error) => new(true);
    private OneOfResult(bool isError) => IsError = isError;
}

public interface IOneOfRequest<TResponse> : IRequest<TResponse>
    where TResponse : struct, IErrorResultFactory<TResponse>;

public sealed class DeleteMenuItemCommand : IOneOfRequest<OneOfResult>;

public sealed class DeleteMenuItemCommandHandler : IValueRequestHandler<DeleteMenuItemCommand, OneOfResult>
{
    public ValueTask<OneOfResult> Handle(DeleteMenuItemCommand request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new OneOfResult());
}

public sealed class UnhandledExceptionBehaviour<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
    where TRequest : class
    where TResponse : struct, IErrorResultFactory<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        ValueRequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (Exception)
        {
            return TResponse.CreateFromError("error");
        }
    }
}
