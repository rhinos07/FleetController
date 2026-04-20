using Microsoft.Extensions.Logging.Abstractions;
using Vda5050FleetController.Application;
using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Application;

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
}
