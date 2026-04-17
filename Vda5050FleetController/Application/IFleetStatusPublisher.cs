namespace Vda5050FleetController.Application;

public interface IFleetStatusPublisher
{
    Task PublishAsync(FleetStatus status, CancellationToken ct = default);
}

public sealed class NoOpFleetStatusPublisher : IFleetStatusPublisher
{
    public static NoOpFleetStatusPublisher Instance { get; } = new();

    private NoOpFleetStatusPublisher()
    {
    }

    public Task PublishAsync(FleetStatus status, CancellationToken ct = default)
        => Task.CompletedTask;
}
