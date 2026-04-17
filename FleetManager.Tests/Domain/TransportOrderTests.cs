using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Domain;

public class TransportOrderTests
{
    [Fact]
    public void Constructor_SetsFieldsCorrectly()
    {
        var order = new TransportOrder("ORD-01", "SRC-A", "DST-B", "LOAD-42");

        Assert.Equal("ORD-01",  order.OrderId);
        Assert.Equal("SRC-A",   order.SourceId);
        Assert.Equal("DST-B",   order.DestId);
        Assert.Equal("LOAD-42", order.LoadId);
    }

    [Fact]
    public void Constructor_SetsInitialStatusToPending()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.Equal(TransportStatus.Pending, order.Status);
    }

    [Fact]
    public void Constructor_LoadIdIsOptional()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.Null(order.LoadId);
    }

    [Fact]
    public void Assign_SetsAssignedVehicleId()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");

        Assert.Equal("Acme/SN-001", order.AssignedVehicleId);
    }

    [Fact]
    public void Assign_SetsStatusToAssigned()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");

        Assert.Equal(TransportStatus.Assigned, order.Status);
    }

    [Fact]
    public void Start_SetsStatusToInProgress()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();

        Assert.Equal(TransportStatus.InProgress, order.Status);
    }

    [Fact]
    public void Complete_SetsStatusToCompleted()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();
        order.Complete();

        Assert.Equal(TransportStatus.Completed, order.Status);
    }

    [Fact]
    public void Fail_SetsStatusToFailed()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();
        order.Fail();

        Assert.Equal(TransportStatus.Failed, order.Status);
    }

    [Fact]
    public void CreatedAt_IsSetAtConstruction()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var order  = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.True(order.CreatedAt >= before);
        Assert.True(order.CreatedAt <= DateTime.UtcNow.AddSeconds(1));
    }
}
