using Vda5050FleetController.Domain.Models;
using Vda5050FleetController.Infrastructure.Mqtt;

namespace FleetManager.Tests.Fakes;

/// <summary>
/// In-memory test double for IVda5050MqttService.
/// Records published messages and lets tests trigger incoming events.
/// </summary>
public sealed class FakeMqttService : IVda5050MqttService
{
    public event Func<VehicleState,      Task> OnStateReceived      = _ => Task.CompletedTask;
    public event Func<ConnectionMessage, Task> OnConnectionReceived  = _ => Task.CompletedTask;

    public List<Order>          PublishedOrders         { get; } = [];
    public List<InstantActions> PublishedInstantActions  { get; } = [];

    public Task ConnectAsync   (CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PublishOrderAsync(Order order, CancellationToken ct = default)
    {
        PublishedOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task PublishInstantActionAsync(InstantActions actions, CancellationToken ct = default)
    {
        PublishedInstantActions.Add(actions);
        return Task.CompletedTask;
    }

    public Task SimulateStateAsync     (VehicleState state)     => OnStateReceived(state);
    public Task SimulateConnectionAsync(ConnectionMessage msg)   => OnConnectionReceived(msg);
}
