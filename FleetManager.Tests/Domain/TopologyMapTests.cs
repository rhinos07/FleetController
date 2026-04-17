using Vda5050FleetController.Domain.Models;

namespace FleetManager.Tests.Domain;

public class TopologyMapTests
{
    private static TopologyMap BuildMap()
    {
        var map = new TopologyMap();
        map.AddNode("SRC", 0.0,  0.0, 0.0, "MAP-1");
        map.AddNode("DST", 10.0, 0.0, 0.0, "MAP-1");
        return map;
    }

    // ── GetNode ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetNode_ReturnsNode_WhenFound()
    {
        var map  = BuildMap();
        var node = map.GetNode("SRC");

        Assert.NotNull(node);
        Assert.Equal(0.0,   node.X);
        Assert.Equal(0.0,   node.Y);
        Assert.Equal("MAP-1", node.MapId);
    }

    [Fact]
    public void GetNode_ReturnsNull_WhenNotFound()
    {
        var map = BuildMap();

        Assert.Null(map.GetNode("DOES-NOT-EXIST"));
    }

    // ── BuildPath ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPath_ReturnsTwoNodes()
    {
        var map            = BuildMap();
        var (nodes, _)     = map.BuildPath("SRC", "DST", [], []);

        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void BuildPath_ReturnsOneEdge()
    {
        var map        = BuildMap();
        var (_, edges) = map.BuildPath("SRC", "DST", [], []);

        Assert.Single(edges);
    }

    [Fact]
    public void BuildPath_SourceNodeAtSequence0()
    {
        var map        = BuildMap();
        var (nodes, _) = map.BuildPath("SRC", "DST", [], []);
        var srcNode    = nodes.Single(n => n.NodeId == "SRC");

        Assert.Equal(0, srcNode.SequenceId);
        Assert.True(srcNode.Released);
    }

    [Fact]
    public void BuildPath_DestNodeAtSequence2()
    {
        var map        = BuildMap();
        var (nodes, _) = map.BuildPath("SRC", "DST", [], []);
        var dstNode    = nodes.Single(n => n.NodeId == "DST");

        Assert.Equal(2, dstNode.SequenceId);
        Assert.True(dstNode.Released);
    }

    [Fact]
    public void BuildPath_EdgeAtSequence1_ConnectingSourceToDest()
    {
        var map        = BuildMap();
        var (_, edges) = map.BuildPath("SRC", "DST", [], []);
        var edge       = edges.Single();

        Assert.Equal(1,     edge.SequenceId);
        Assert.Equal("SRC", edge.StartNodeId);
        Assert.Equal("DST", edge.EndNodeId);
        Assert.True(edge.Released);
    }

    [Fact]
    public void BuildPath_EdgeHasMaxSpeed()
    {
        var map        = BuildMap();
        var (_, edges) = map.BuildPath("SRC", "DST", [], []);

        Assert.Equal(1.5, edges.Single().MaxSpeed);
    }

    [Fact]
    public void BuildPath_AttachesPickActionsToSourceNode()
    {
        var map        = BuildMap();
        var pick       = new List<VdaAction> { new() { ActionId = "pick-1", ActionType = "pick" } };
        var (nodes, _) = map.BuildPath("SRC", "DST", pick, []);
        var srcNode    = nodes.Single(n => n.NodeId == "SRC");

        Assert.Single(srcNode.Actions);
        Assert.Equal("pick", srcNode.Actions[0].ActionType);
    }

    [Fact]
    public void BuildPath_AttachesDropActionsToDestNode()
    {
        var map        = BuildMap();
        var drop       = new List<VdaAction> { new() { ActionId = "drop-1", ActionType = "drop" } };
        var (nodes, _) = map.BuildPath("SRC", "DST", [], drop);
        var dstNode    = nodes.Single(n => n.NodeId == "DST");

        Assert.Single(dstNode.Actions);
        Assert.Equal("drop", dstNode.Actions[0].ActionType);
    }

    [Fact]
    public void BuildPath_NodePositionsMatchTopologyData()
    {
        var map        = BuildMap();
        var (nodes, _) = map.BuildPath("SRC", "DST", [], []);

        var srcPos = nodes.Single(n => n.NodeId == "SRC").NodePosition;
        var dstPos = nodes.Single(n => n.NodeId == "DST").NodePosition;

        Assert.NotNull(srcPos);
        Assert.Equal(0.0,  srcPos.X);
        Assert.NotNull(dstPos);
        Assert.Equal(10.0, dstPos.X);
    }

    [Fact]
    public void BuildPath_ThrowsInvalidOperation_WhenSourceNodeMissing()
    {
        var map = BuildMap();

        Assert.Throws<InvalidOperationException>(
            () => map.BuildPath("MISSING", "DST", [], []));
    }

    [Fact]
    public void BuildPath_ThrowsInvalidOperation_WhenDestNodeMissing()
    {
        var map = BuildMap();

        Assert.Throws<InvalidOperationException>(
            () => map.BuildPath("SRC", "MISSING", [], []));
    }
}
