using Vda5050FleetController.Application.Contracts;

namespace Vda5050FleetController.Application;

/// <summary>
/// Interface for publishing fleet status updates to connected clients.
/// </summary>
public interface IFleetStatusPublisher
{
    /// <summary>
    /// Publishes the current fleet status to all subscribers.
    /// </summary>
    /// <param name="status">The fleet status to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(FleetStatus status, CancellationToken ct = default);
}

/// <summary>
/// No-operation implementation of IFleetStatusPublisher.
/// Used when no real-time publishing is required (e.g., in tests).
/// </summary>
public sealed class NoOpFleetStatusPublisher : IFleetStatusPublisher
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static NoOpFleetStatusPublisher Instance { get; } = new();

    private NoOpFleetStatusPublisher()
    {
    }

    public Task PublishAsync(FleetStatus status, CancellationToken ct = default)
        => Task.CompletedTask;
}
