using System.Collections.Concurrent;

namespace Mediator.Switch.Tests;

public class NotificationTracker
{
    public ConcurrentQueue<string> ExecutionOrder { get; } = new();
}