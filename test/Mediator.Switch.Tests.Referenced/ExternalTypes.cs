namespace Mediator.Switch.Tests.Referenced;

public record UserDto(int UserId, string Description, int Version);

[RequestHandler(typeof(ExternalGetUserRequestHandler))]
public sealed class ExternalGetUserRequest : IRequest<UserDto>
{
    public int UserId { get; }

    public ExternalGetUserRequest(int userId) => UserId = userId;
}

public class ExternalGetUserRequestHandler : IRequestHandler<ExternalGetUserRequest, UserDto>
{
    public Task<UserDto> Handle(ExternalGetUserRequest request, CancellationToken cancellationToken = default)
    {
        // Return a simple deterministic DTO so test can assert values
        var dto = new UserDto(request.UserId, $"User {request.UserId}", 51);
        return Task.FromResult(dto);
    }
}