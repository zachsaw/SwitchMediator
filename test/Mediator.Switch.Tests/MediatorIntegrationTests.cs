using FluentValidation;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Mediator.Switch.Tests.Referenced;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

public class MediatorIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ISender _sender;
    private readonly IPublisher _publisher;
    private readonly NotificationTracker _notificationTracker;

    public MediatorIntegrationTests()
    {
        static void ConfigureMediator(SwitchMediatorOptions op)
        {
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(TestUserLoggedInLogger) // Logger first
                // Analytics handler is automatically appended
            );
        }

        var setupResult = MediatorTestSetup.Setup(
            configureMediator: ConfigureMediator,
            lifetime: ServiceLifetime.Scoped
        );

        _serviceProvider = setupResult.ServiceProvider;
        _scope = setupResult.Scope;
        _sender = setupResult.Sender;
        _publisher = setupResult.Publisher;
        _notificationTracker = setupResult.Tracker;
    }

    [Fact]
    public async Task Send_GetUserRequest_Success_ReturnsUserAndPublishesEvent()
    {
        // Arrange
        var request = new GetUserRequest(123);
        var initialVersion = 50;
        var expectedVersionAfterPipeline = initialVersion + 1;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(request.UserId, result.Value.UserId);
        Assert.Contains($"User {request.UserId}", result.Value.Description);
        Assert.Equal(expectedVersionAfterPipeline, result.Value.Version);

        // Assert notifications using the field _notificationTracker
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on constructor config
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_Success_ReturnsOrderId()
    {
        // Arrange
        var request = new CreateOrderRequest("TestProduct");
        var expectedOrderId = 42;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.Equal(expectedOrderId, result);
    }

    [Fact]
    public async Task Send_GetUserRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new GetUserRequest(-1);

        // Act & Assert
        // Use the field _sender
        var exception = await Assert.ThrowsAsync<ValidationException>(() => _sender.Send(request));
        Assert.Single(exception.Errors);
        Assert.Equal("UserId must be positive", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(GetUserRequest.UserId), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateOrderRequest("");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() => _sender.Send(request));
        Assert.Single(exception.Errors);
        Assert.Equal("Product cannot be empty", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(CreateOrderRequest.Product), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Publish_UserLoggedInEvent_ExecutesHandlersInCorrectOrder()
    {
        // Arrange
        var notification = new UserLoggedInEvent(999);
        _notificationTracker.ExecutionOrder.Clear(); // Clear tracker before publish if needed

        // Act
        await _publisher.Publish(notification); // Use the field _publisher

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on constructor config
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Publish_DerivedUserLoggedInEvent_UsesUserLoggedInEventHandler_AsFallback()
    {
        // Arrange
        var notification = new DerivedUserLoggedInEvent(999);
         _notificationTracker.ExecutionOrder.Clear(); // Clear tracker

        // Act
        await _publisher.Publish(notification); // Use the field _publisher

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler);
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }


    [Fact]
    public async Task Send_GetUserRequest_AuditAndVersionBehaviorsRun()
    {
        // Arrange
        var request = new GetUserRequest(200);
        var initialVersion = 50;
        var expectedVersion = initialVersion + 1;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedVersion, result.Value.Version);
        // Audit check remains implicit
    }

    [Fact]
    public async Task Send_CreateOrderRequest_TransactionBehaviorRuns_ButNotAuditOrVersion()
    {
        // Arrange
        var request = new CreateOrderRequest("Gadget");

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.Equal(42, result);
        // Transaction check remains implicit
    }

    [Fact]
    public async Task Send_DogQuery_UsesSpecificDogQueryHandler()
    {
        // Arrange
        var dogName = "Rex";
        var dogBreed = "German Shepherd";
        var request = new DogQuery(dogName, dogBreed);

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("Handled by DogQueryHandler:", result);
        Assert.Contains($"Dog named {dogName}", result);
        Assert.Contains($"Breed: {dogBreed}", result);
    }

    [Fact]
    public async Task Send_CatQuery_UsesBaseAnimalQueryHandler_AsFallback()
    {
        // Arrange
        var catName = "Whiskers";
        var request = new CatQuery(catName);

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("Handled by AnimalQueryHandler:", result);
        Assert.Contains($"Generic animal named {catName}", result);
        Assert.DoesNotContain("Dog", result);
        Assert.DoesNotContain("Cat:", result);
    }

    [Fact]
    public async Task Publish_StartProcessNotification_InvokesCorrectHandlerInMultiHandlerClass()
    {
        // Arrange
        var processId = Guid.NewGuid();
        var notification = new StartProcessNotification(processId);
        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        await _publisher.Publish(notification);

        // Assert
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiProcessNotificationHandler)}::{nameof(StartProcessNotification)}::{processId}", handlerInfo);
    }

    [Fact]
    public async Task Publish_EndProcessNotification_InvokesCorrectHandlerInMultiHandlerClass()
    {
        // Arrange
        var processId = Guid.NewGuid();
        var success = true;
        var notification = new EndProcessNotification(processId, success);
        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        await _publisher.Publish(notification);

        // Assert
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiProcessNotificationHandler)}::{nameof(EndProcessNotification)}::{processId}::{success}", handlerInfo);
    }

    [Fact]
    public async Task Publish_MonitorProcessNotification_InvokesCorrectHandlerInMultiHandlerClass() // If using the third notification
    {
        // Arrange
        var processId = Guid.NewGuid();
        var progress = 0.75;
        var notification = new MonitorProcessNotification(processId, progress);
        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        await _publisher.Publish(notification);

        // Assert
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiProcessNotificationHandler)}::{nameof(MonitorProcessNotification)}::{processId}::{progress}", handlerInfo);
    }

    [Fact]
    public async Task Publish_MultipleDistinctNotifications_InvokesCorrectHandlersInMultiHandlerClass()
    {
        // Arrange
        var processId1 = Guid.NewGuid();
        var processId2 = Guid.NewGuid();
        var startNotification = new StartProcessNotification(processId1);
        var endNotification = new EndProcessNotification(processId2, false);
        var monitorNotification = new MonitorProcessNotification(processId1, 0.5); // Optional

        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        await _publisher.Publish(startNotification);
        await _publisher.Publish(endNotification);
        await _publisher.Publish(monitorNotification); // Optional

        // Assert
        var expectedCount = 3; // Change to 2 if not using MonitorProcessNotification
        Assert.Equal(expectedCount, _notificationTracker.ExecutionOrder.Count);

        var executedHandlers = _notificationTracker.ExecutionOrder.ToList(); // Easier to assert contents

        Assert.Contains($"{nameof(MultiProcessNotificationHandler)}::{nameof(StartProcessNotification)}::{processId1}", executedHandlers);
        Assert.Contains($"{nameof(MultiProcessNotificationHandler)}::{nameof(EndProcessNotification)}::{processId2}::False", executedHandlers);
        Assert.Contains($"{nameof(MultiProcessNotificationHandler)}::{nameof(MonitorProcessNotification)}::{processId1}::0.5", executedHandlers); // Optional
    }

    [Fact]
    public async Task Send_ProcessDataCommand_InvokesCorrectHandlerInMultiHandlerClass()
    {
        // Arrange
        var command = new ProcessDataCommand("test data");
        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        var result = await _sender.Send(command);

        // Assert
        // 1. Check the response
        Assert.True(result.IsSuccess);
        Assert.Equal("Processed: TEST DATA", result.Value);

        // 2. Check the tracker
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiRequestTypeHandler)}::{nameof(ProcessDataCommand)}::{command.Data}", handlerInfo);
    }

    [Fact]
    public async Task Send_CalculateValueQuery_InvokesCorrectHandlerInMultiHandlerClass()
    {
        // Arrange
        var query = new CalculateValueQuery(21);
         _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        var result = await _sender.Send(query);

        // Assert
        // 1. Check the response
        Assert.Equal(42, result); // 21 * 2

        // 2. Check the tracker
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiRequestTypeHandler)}::{nameof(CalculateValueQuery)}::{query.Input}", handlerInfo);
    }

    [Fact]
    public async Task Send_GetConfigurationRequest_InvokesCorrectHandlerInMultiHandlerClass() // Optional
    {
        // Arrange
        var request = new GetConfigurationRequest("TIMEOUT");
        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        var result = await _sender.Send(request);

        // Assert
        // 1. Check the response
        Assert.Equal("30000", result);

        // 2. Check the tracker
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var handlerInfo));
        Assert.Equal($"{nameof(MultiRequestTypeHandler)}::{nameof(GetConfigurationRequest)}::{request.Key}", handlerInfo);
    }

    [Fact]
    public async Task Send_MultipleDistinctRequests_InvokesCorrectHandlersInMultiHandlerClass()
    {
        // Arrange
        var command = new ProcessDataCommand("first");
        var query = new CalculateValueQuery(10);
        var configRequest = new GetConfigurationRequest("OTHER"); // Optional

        _notificationTracker.ExecutionOrder.Clear(); // Ensure clean state

        // Act
        var commandResult = await _sender.Send(command);
        var queryResult = await _sender.Send(query);
        var configResult = await _sender.Send(configRequest); // Optional

        // Assert
        // 1. Check responses (optional, but good practice)
        Assert.True(commandResult.IsSuccess);
        Assert.Equal("Processed: FIRST", commandResult.Value);
        Assert.Equal(20, queryResult);
        Assert.Null(configResult); // Optional

        // 2. Check the tracker for distinct calls
        var expectedCount = 3; // Change to 2 if not using GetConfigurationRequest
        Assert.Equal(expectedCount, _notificationTracker.ExecutionOrder.Count);

        var executedHandlers = _notificationTracker.ExecutionOrder.ToList();
        Assert.Contains($"{nameof(MultiRequestTypeHandler)}::{nameof(ProcessDataCommand)}::{command.Data}", executedHandlers);
        Assert.Contains($"{nameof(MultiRequestTypeHandler)}::{nameof(CalculateValueQuery)}::{query.Input}", executedHandlers);
        Assert.Contains($"{nameof(MultiRequestTypeHandler)}::{nameof(GetConfigurationRequest)}::{configRequest.Key}", executedHandlers); // Optional
    }

    [Fact]
    public async Task Send_ExternalGetUserRequest_FromReferencedProject_Works()
    {
        // Arrange
        var request = new ExternalGetUserRequest(123);

        // Act
        var result = await _sender.Send(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123, result.UserId);
        Assert.Contains("User", result.Description);
        Assert.Equal(51, result.Version);
    }

    [Fact]
    public async Task Send_ExternalGetUserRequest_ReferencedHandler_HandlesMultipleRequests()
    {
        // Arrange
        var req1 = new ExternalGetUserRequest(10);
        var req2 = new ExternalGetUserRequest(20);

        // Act - send both concurrently to ensure handler from referenced project is discovered and works under concurrency
        var task1 = _sender.Send(req1);
        var task2 = _sender.Send(req2);

        var result1 = await task1;
        var result2 = await task2;

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(10, result1.UserId);
        Assert.Equal(20, result2.UserId);
        Assert.Contains("User", result1.Description);
        Assert.Contains("User", result2.Description);
        Assert.Equal(51, result1.Version);
        Assert.Equal(51, result2.Version);
        Assert.NotEqual(result1.Description, result2.Description);
    }

    [Fact]
    public async Task Publish_AlertNotification_RunsBehaviorsInCorrectOrder()
    {
        // Arrange
        var notification = new AlertNotification("System Critical");
        _notificationTracker.ExecutionOrder.Clear();

        // Act
        await _publisher.Publish(notification);

        // Assert
        // Expected flow: Outer Start -> Inner Start -> Handler -> Inner End -> Outer End
        Assert.Equal(5, _notificationTracker.ExecutionOrder.Count);

        var events = _notificationTracker.ExecutionOrder.ToArray();
        Assert.Equal("Outer: Start", events[0]);
        Assert.Equal("Inner: Start", events[1]);
        Assert.Equal($"Handler: {notification.Message}", events[2]);
        Assert.Equal("Inner: End", events[3]);
        Assert.Equal("Outer: End", events[4]);
    }

    [Fact]
    public async Task Publish_FragileNotification_ResilientBehaviorSwallowsException()
    {
        // Arrange
        var notification = new FragileNotification();
        _notificationTracker.ExecutionOrder.Clear();

        // Act
        // This should NOT throw because ResilientNotificationBehavior catches it
        await _publisher.Publish(notification);

        // Assert
        Assert.Single(_notificationTracker.ExecutionOrder);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var log));
        Assert.Equal("Caught: Handler Boom!", log);
    }

    [Fact]
    public async Task Publish_SecureNotification_RunsConstrainedBehavior()
    {
        // Arrange
        var notification = new SecureNotification();
        _notificationTracker.ExecutionOrder.Clear();

        // Act
        await _publisher.Publish(notification);

        // Assert
        // Should see the Security Audit log AND the handler log
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);

        var events = _notificationTracker.ExecutionOrder.ToArray();
        Assert.Equal("SecurityAudit: Checked", events[0]);
        Assert.Equal("SecureHandler", events[1]);
    }

    [Fact]
    public async Task Publish_PublicNotification_SkipsConstrainedBehavior()
    {
        // Arrange
        var notification = new PublicNotification();
        _notificationTracker.ExecutionOrder.Clear();

        // Act
        await _publisher.Publish(notification);

        // Assert
        // Should ONLY see the handler log, NO Security Audit log
        Assert.Single(_notificationTracker.ExecutionOrder);

        var events = _notificationTracker.ExecutionOrder.ToArray();
        Assert.Equal("PublicHandler", events[0]);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}