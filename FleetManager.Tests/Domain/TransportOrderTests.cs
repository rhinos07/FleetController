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
    public void Constructor_ThrowsArgumentException_WhenOrderIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new TransportOrder(null!, "SRC", "DST"));
        Assert.Throws<ArgumentException>(() => new TransportOrder("", "SRC", "DST"));
        Assert.Throws<ArgumentException>(() => new TransportOrder("  ", "SRC", "DST"));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenSourceIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new TransportOrder("ORD-01", null!, "DST"));
        Assert.Throws<ArgumentException>(() => new TransportOrder("ORD-01", "", "DST"));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenDestIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new TransportOrder("ORD-01", "SRC", null!));
        Assert.Throws<ArgumentException>(() => new TransportOrder("ORD-01", "SRC", ""));
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
    public void Assign_SetsAssignedAtTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var order  = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");

        Assert.NotNull(order.AssignedAt);
        Assert.True(order.AssignedAt >= before);
    }

    [Fact]
    public void Assign_ThrowsInvalidOperationException_WhenNotPending()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");

        Assert.Throws<InvalidOperationException>(() => order.Assign("Acme/SN-002"));
    }

    [Fact]
    public void Assign_ThrowsArgumentException_WhenVehicleIdIsNullOrEmpty()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.Throws<ArgumentException>(() => order.Assign(null!));
        Assert.Throws<ArgumentException>(() => order.Assign(""));
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
    public void Start_SetsStartedAtTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var order  = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();

        Assert.NotNull(order.StartedAt);
        Assert.True(order.StartedAt >= before);
    }

    [Fact]
    public void Start_ThrowsInvalidOperationException_WhenNotAssigned()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.Throws<InvalidOperationException>(() => order.Start());
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
    public void Complete_SetsCompletedAtTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var order  = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();
        order.Complete();

        Assert.NotNull(order.CompletedAt);
        Assert.True(order.CompletedAt >= before);
    }

    [Fact]
    public void Complete_ThrowsInvalidOperationException_WhenNotInProgress()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");

        Assert.Throws<InvalidOperationException>(() => order.Complete());
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
    public void Fail_CanBeCalledFromPendingStatus()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Fail();

        Assert.Equal(TransportStatus.Failed, order.Status);
    }

    [Fact]
    public void Fail_CanBeCalledFromAssignedStatus()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Fail();

        Assert.Equal(TransportStatus.Failed, order.Status);
    }

    [Fact]
    public void Fail_ThrowsInvalidOperationException_WhenAlreadyCompleted()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Assign("Acme/SN-001");
        order.Start();
        order.Complete();

        Assert.Throws<InvalidOperationException>(() => order.Fail());
    }

    [Fact]
    public void Fail_ThrowsInvalidOperationException_WhenAlreadyFailed()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        order.Fail();

        Assert.Throws<InvalidOperationException>(() => order.Fail());
    }

    [Fact]
    public void IsActive_TrueForPendingAssignedInProgress()
    {
        var pending = new TransportOrder("ORD-01", "SRC", "DST");
        Assert.True(pending.IsActive);

        pending.Assign("Acme/SN-001");
        Assert.True(pending.IsActive);

        pending.Start();
        Assert.True(pending.IsActive);
    }

    [Fact]
    public void IsActive_FalseForCompletedAndFailed()
    {
        var completed = new TransportOrder("ORD-01", "SRC", "DST");
        completed.Assign("Acme/SN-001");
        completed.Start();
        completed.Complete();
        Assert.False(completed.IsActive);

        var failed = new TransportOrder("ORD-02", "SRC", "DST");
        failed.Fail();
        Assert.False(failed.IsActive);
    }

    [Fact]
    public void IsFinalized_TrueForCompletedAndFailed()
    {
        var completed = new TransportOrder("ORD-01", "SRC", "DST");
        completed.Assign("Acme/SN-001");
        completed.Start();
        completed.Complete();
        Assert.True(completed.IsFinalized);

        var failed = new TransportOrder("ORD-02", "SRC", "DST");
        failed.Fail();
        Assert.True(failed.IsFinalized);
    }

    [Fact]
    public void IsFinalized_FalseForActiveOrders()
    {
        var order = new TransportOrder("ORD-01", "SRC", "DST");
        Assert.False(order.IsFinalized);

        order.Assign("Acme/SN-001");
        Assert.False(order.IsFinalized);

        order.Start();
        Assert.False(order.IsFinalized);
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
