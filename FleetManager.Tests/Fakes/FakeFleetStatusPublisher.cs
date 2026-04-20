using Vda5050FleetController.Application;
using Vda5050FleetController.Application.Contracts;

namespace FleetManager.Tests.Fakes;

public class FakeFleetStatusPublisher : IFleetStatusPublisher
{
    public List<FleetStatus> PublishedStatuses { get; } = [];

    public Task PublishAsync(FleetStatus status, CancellationToken ct = default)
    {
        PublishedStatuses.Add(status);
        return Task.CompletedTask;
    }
}
