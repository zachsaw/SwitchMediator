using Mediator.Switch;
using System.Threading.Tasks;

namespace Test.MultipleRequests;

// First Request
public class GetProductRequest : IRequest<string>
{
    public int ProductId { get; set; }
}

public class GetProductRequestHandler : IRequestHandler<GetProductRequest, string>
{
    public Task<string> Handle(GetProductRequest request) => Task.FromResult($"Product {request.ProductId}");
}

// Second Request
public class GetInventoryRequest : IRequest<int>
{
    public string Sku { get; set; } = "";
}

public class GetInventoryRequestHandler : IRequestHandler<GetInventoryRequest, int>
{
    public Task<int> Handle(GetInventoryRequest request) => Task.FromResult(request.Sku?.Length ?? 0); // Dummy logic
}