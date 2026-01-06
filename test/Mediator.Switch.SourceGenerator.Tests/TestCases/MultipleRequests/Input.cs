using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.MultipleRequests;

[SwitchMediator]
public partial class TestMediator;

// First Request
public class GetProductRequest : IRequest<string>
{
    public int ProductId { get; set; }
}

public class GetProductRequestHandler : IRequestHandler<GetProductRequest, string>
{
    public Task<string> Handle(GetProductRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult($"Product {request.ProductId}");
}

// Second Request
public class GetInventoryRequest : IRequest<int>
{
    public string Sku { get; set; } = "";
}

public class GetInventoryRequestHandler : IRequestHandler<GetInventoryRequest, int>
{
    public Task<int> Handle(GetInventoryRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(request.Sku?.Length ?? 0); // Dummy logic
}