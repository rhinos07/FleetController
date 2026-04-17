using FleetManager.Api.Models;

namespace FleetManager.Api.Services;

public sealed class RouteGraphService
{
    private readonly Dictionary<string, RouteNode> _nodes;
    private readonly List<RouteEdge> _edges;
    private readonly HashSet<string> _blockedZones = new(StringComparer.OrdinalIgnoreCase);

    public RouteGraphService()
    {
        _nodes = new Dictionary<string, RouteNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["INBOUND"] = new("INBOUND", "ZONE-IN"),
            ["BUFFER-1"] = new("BUFFER-1", "ZONE-A"),
            ["BUFFER-2"] = new("BUFFER-2", "ZONE-B"),
            ["OUTBOUND"] = new("OUTBOUND", "ZONE-OUT")
        };

        _edges =
        [
            new("INBOUND", "BUFFER-1"),
            new("BUFFER-1", "BUFFER-2"),
            new("BUFFER-2", "OUTBOUND")
        ];
    }

    public IReadOnlyCollection<RouteNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<RouteEdge> Edges => _edges;
    public IReadOnlyCollection<string> BlockedZones => _blockedZones;

    public bool HasNode(string nodeId) => _nodes.ContainsKey(nodeId);

    public bool IsZoneBlockedByNode(string nodeId)
        => _nodes.TryGetValue(nodeId, out var node) && _blockedZones.Contains(node.ZoneId);

    public void SetZoneBlocked(string zoneId, bool blocked)
    {
        if (blocked)
        {
            _blockedZones.Add(zoneId);
            return;
        }

        _blockedZones.Remove(zoneId);
    }

    public IReadOnlyList<string>? TryFindRoute(string sourceNodeId, string destinationNodeId)
    {
        var queue = new Queue<string>();
        var parents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(sourceNodeId);
        parents[sourceNodeId] = null;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, destinationNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return BuildPath(destinationNodeId, parents);
            }

            foreach (var edge in _edges.Where(edge => string.Equals(edge.FromNodeId, current, StringComparison.OrdinalIgnoreCase)))
            {
                if (parents.ContainsKey(edge.ToNodeId) || IsZoneBlockedByNode(edge.ToNodeId))
                {
                    continue;
                }

                parents[edge.ToNodeId] = current;
                queue.Enqueue(edge.ToNodeId);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildPath(string destinationNodeId, IReadOnlyDictionary<string, string?> parents)
    {
        var path = new List<string>();
        string? current = destinationNodeId;

        while (current is not null)
        {
            path.Add(current);
            current = parents[current];
        }

        path.Reverse();
        return path;
    }
}
