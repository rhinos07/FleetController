using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;

namespace FleetController.Tests.Application;

public class TransportOrderQueueTests
{
    private static TransportOrderQueue CreateQueue() =>
        new(NullLogger<TransportOrderQueue>.Instance);

    private static TransportOrder MakeOrder(string id = "ORD-01") =>
        new(id, "SRC", "DST");

    [Fact]
    public void Enqueue_IncreasesPendingCount()
    {
        var queue = CreateQueue();
        queue.Enqueue(MakeOrder());

        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void DequeuePending_ReturnsOrder_WhenQueueHasItems()
    {
        var queue = CreateQueue();
        var order = MakeOrder();
        queue.Enqueue(order);

        var dequeued = queue.DequeuePending();

        Assert.Same(order, dequeued);
    }

    [Fact]
    public void DequeuePending_DecreasesPendingCount()
    {
        var queue = CreateQueue();
        queue.Enqueue(MakeOrder());
        queue.DequeuePending();

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void DequeuePending_ReturnsNull_WhenQueueIsEmpty()
    {
        var queue = CreateQueue();

        Assert.Null(queue.DequeuePending());
    }

    [Fact]
    public void DequeuePending_IsFifo()
    {
        var queue  = CreateQueue();
        var first  = MakeOrder("ORD-01");
        var second = MakeOrder("ORD-02");
        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.Same(first,  queue.DequeuePending());
        Assert.Same(second, queue.DequeuePending());
    }

    [Fact]
    public void MarkActive_IncreasesActiveCount()
    {
        var queue = CreateQueue();
        var order = MakeOrder();
        queue.Enqueue(order);
        queue.DequeuePending();
        queue.MarkActive(order);

        Assert.Equal(1, queue.ActiveCount);
    }

    [Fact]
    public void FindActive_ReturnsActiveOrder()
    {
        var queue = CreateQueue();
        var order = MakeOrder("ORD-42");
        queue.MarkActive(order);

        var found = queue.FindActive("ORD-42");

        Assert.Same(order, found);
    }

    [Fact]
    public void FindActive_ReturnsNull_WhenOrderNotActive()
    {
        var queue = CreateQueue();

        Assert.Null(queue.FindActive("DOES-NOT-EXIST"));
    }

    [Fact]
    public void Complete_RemovesOrderFromActive()
    {
        var queue = CreateQueue();
        var order = MakeOrder("ORD-01");
        order.Assign("Acme/SN-001");
        order.Start();
        queue.MarkActive(order);
        queue.Complete("ORD-01");

        Assert.Equal(0,   queue.ActiveCount);
        Assert.Null(queue.FindActive("ORD-01"));
    }

    [Fact]
    public void Complete_SetsOrderStatusToCompleted()
    {
        var queue = CreateQueue();
        var order = MakeOrder("ORD-01");
        order.Assign("Acme/SN-001");
        order.Start();
        queue.MarkActive(order);

        queue.Complete("ORD-01");

        Assert.Equal(TransportStatus.Completed, order.Status);
    }

    [Fact]
    public void Complete_DoesNotThrow_WhenOrderIdDoesNotExist()
    {
        var queue = CreateQueue();

        var ex = Record.Exception(() => queue.Complete("GHOST-ORDER"));
        Assert.Null(ex);
    }

    [Fact]
    public void ActiveCount_IsZeroInitially()
    {
        Assert.Equal(0, CreateQueue().ActiveCount);
    }

    [Fact]
    public void PendingCount_IsZeroInitially()
    {
        Assert.Equal(0, CreateQueue().PendingCount);
    }

    // ── RemovePending ─────────────────────────────────────────────────────────

    [Fact]
    public void RemovePending_ReturnsTrue_WhenOrderIsInQueue()
    {
        var queue = CreateQueue();
        queue.Enqueue(MakeOrder("ORD-01"));

        var result = queue.RemovePending("ORD-01");

        Assert.True(result);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void RemovePending_ReturnsFalse_WhenOrderNotInQueue()
    {
        var queue = CreateQueue();

        var result = queue.RemovePending("DOES-NOT-EXIST");

        Assert.False(result);
    }

    [Fact]
    public void RemovePending_PreservesOtherOrders()
    {
        var queue  = CreateQueue();
        var first  = MakeOrder("ORD-01");
        var second = MakeOrder("ORD-02");
        var third  = MakeOrder("ORD-03");
        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(third);

        queue.RemovePending("ORD-02");

        Assert.Equal(2, queue.PendingCount);
        Assert.Same(first, queue.DequeuePending());
        Assert.Same(third, queue.DequeuePending());
    }

    [Fact]
    public void RemovePending_ReturnsFalse_WhenOrderIsActiveNotPending()
    {
        var queue = CreateQueue();
        var order = MakeOrder("ORD-01");
        queue.MarkActive(order);

        var result = queue.RemovePending("ORD-01");

        Assert.False(result);
        Assert.Equal(1, queue.ActiveCount);
    }

    // ── ReplacePending ────────────────────────────────────────────────────────

    [Fact]
    public void ReplacePending_ReturnsTrue_AndUpdatesOrder()
    {
        var queue       = CreateQueue();
        var original    = MakeOrder("ORD-01");
        var replacement = new TransportOrder("ORD-01", "NEW-SRC", "NEW-DST", "LOAD-X");
        queue.Enqueue(original);

        var result = queue.ReplacePending("ORD-01", replacement);

        Assert.True(result);
        var dequeued = queue.DequeuePending();
        Assert.Same(replacement, dequeued);
        Assert.Equal("NEW-SRC", dequeued!.SourceId);
        Assert.Equal("NEW-DST", dequeued.DestId);
        Assert.Equal("LOAD-X",  dequeued.LoadId);
    }

    [Fact]
    public void ReplacePending_ReturnsFalse_WhenOrderNotInQueue()
    {
        var queue       = CreateQueue();
        var replacement = new TransportOrder("ORD-99", "NEW-SRC", "NEW-DST");

        var result = queue.ReplacePending("ORD-99", replacement);

        Assert.False(result);
    }

    [Fact]
    public void ReplacePending_PreservesQueueOrder()
    {
        var queue       = CreateQueue();
        var first       = MakeOrder("ORD-01");
        var second      = MakeOrder("ORD-02");
        var third       = MakeOrder("ORD-03");
        var replacement = new TransportOrder("ORD-02", "X", "Y");
        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(third);

        queue.ReplacePending("ORD-02", replacement);

        Assert.Same(first,       queue.DequeuePending());
        Assert.Same(replacement, queue.DequeuePending());
        Assert.Same(third,       queue.DequeuePending());
    }
}
