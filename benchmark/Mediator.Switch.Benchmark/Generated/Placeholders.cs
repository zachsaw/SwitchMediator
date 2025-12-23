// ReSharper disable InconsistentNaming
// ReSharper disable RedundantNameQualifier
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace Mediator.Switch.Benchmark.Generated
{
    // --- MediatR Placeholders ---
    public class Ping1Request_MediatR : MediatR.IRequest<Pong1Response_MediatR> { public int Id { get; set; } }
    public class Pong1Response_MediatR { public int Id { get; set; } }
    public class Ping1RequestHandler_MediatR : MediatR.IRequestHandler<Ping1Request_MediatR, Pong1Response_MediatR>
    {
        public Task<Pong1Response_MediatR> Handle(Ping1Request_MediatR request, CancellationToken cancellationToken) =>
            Task.FromResult(new Pong1Response_MediatR { Id = request.Id });
    }
    public class Notify1Event_MediatR : MediatR.INotification { public string Message { get; set; } = "Placeholder"; }
    public class Notify1EventHandler1_MediatR : MediatR.INotificationHandler<Notify1Event_MediatR>
    {
        public Task Handle(Notify1Event_MediatR notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // --- SwitchMediator Placeholders ---
    public class Ping1Request_Switch : Mediator.Switch.IRequest<Pong1Response_Switch> { public int Id { get; set; } }
    public class Pong1Response_Switch { public int Id { get; set; } }
    public class Ping1RequestHandler_Switch : Mediator.Switch.IRequestHandler<Ping1Request_Switch, Pong1Response_Switch>
    {
        public Task<Pong1Response_Switch> Handle(Ping1Request_Switch request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Pong1Response_Switch { Id = request.Id });
    }
    public class Notify1Event_Switch : Mediator.Switch.INotification { public string Message { get; set; } = "Placeholder"; }
    public class Notify1EventHandler1_Switch : Mediator.Switch.INotificationHandler<Notify1Event_Switch>
    {
        public Task Handle(Notify1Event_Switch notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}