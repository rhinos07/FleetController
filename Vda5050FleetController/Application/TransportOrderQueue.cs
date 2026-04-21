using Microsoft.Extensions.Logging;
using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Application;

public class TransportOrderQueue
{
    private readonly List<TransportOrder>               _pending = [];
    private readonly Dictionary<string, TransportOrder> _active  = [];
    private readonly ILogger<TransportOrderQueue>       _log;

    public TransportOrderQueue(ILogger<TransportOrderQueue> log) => _log = log;

    public void Enqueue(TransportOrder order)
    {
        _pending.Add(order);
        _log.LogInformation("Queued TransportOrder {OrderId}: {Src} → {Dst}",
            order.OrderId, order.SourceId, order.DestId);
    }

    public TransportOrder? DequeuePending()
    {
        if (_pending.Count == 0) return null;
        var order = _pending[0];
        _pending.RemoveAt(0);
        return order;
    }

    public void MarkActive(TransportOrder order)
        => _active[order.OrderId] = order;

    public TransportOrder? FindActive(string orderId)
        => _active.GetValueOrDefault(orderId);

    public bool RemovePending(string orderId)
    {
        var index = _pending.FindIndex(o => o.OrderId == orderId);
        if (index < 0) return false;
        _pending.RemoveAt(index);
        _log.LogInformation("Cancelled pending TransportOrder {OrderId}", orderId);
        return true;
    }

    public bool ReplacePending(string orderId, TransportOrder replacement)
    {
        var index = _pending.FindIndex(o => o.OrderId == orderId);
        if (index < 0) return false;
        _pending[index] = replacement;
        _log.LogInformation("Updated pending TransportOrder {OrderId}: {Src} → {Dst}",
            replacement.OrderId, replacement.SourceId, replacement.DestId);
        return true;
    }

    public void Complete(string orderId)
    {
        if (_active.Remove(orderId, out var order))
        {
            order.Complete();
            _log.LogInformation("TransportOrder {OrderId} completed", orderId);
        }
    }

    public IEnumerable<TransportOrder> GetAllOrders()
        => _pending.Concat(_active.Values);

    public int PendingCount => _pending.Count;
    public int ActiveCount  => _active.Count;
}
