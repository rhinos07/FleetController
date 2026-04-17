using Vda5050FleetController.Infrastructure.Mqtt;

namespace FleetManager.Tests.Infrastructure;

public class Vda5050TopicTests
{
    private static MqttOptions DefaultOptions() => new()
    {
        InterfaceName = "uagv",
        MajorVersion  = "v2"
    };

    [Fact]
    public void Build_ReturnsCorrectFormat()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.Build(opts, "Acme", "SN-001", "state");

        Assert.Equal("uagv/v2/Acme/SN-001/state", topic);
    }

    [Fact]
    public void Order_ReturnsOrderTopic()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.Order(opts, "Acme", "SN-001");

        Assert.Equal("uagv/v2/Acme/SN-001/order", topic);
    }

    [Fact]
    public void InstantAction_ReturnsInstantActionsTopic()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.InstantAction(opts, "Acme", "SN-001");

        Assert.Equal("uagv/v2/Acme/SN-001/instantActions", topic);
    }

    [Fact]
    public void State_ReturnsStateTopic()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.State(opts, "Acme", "SN-001");

        Assert.Equal("uagv/v2/Acme/SN-001/state", topic);
    }

    [Fact]
    public void Connection_ReturnsConnectionTopic()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.Connection(opts, "Acme", "SN-001");

        Assert.Equal("uagv/v2/Acme/SN-001/connection", topic);
    }

    [Fact]
    public void Visualization_ReturnsVisualizationTopic()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.Visualization(opts, "Acme", "SN-001");

        Assert.Equal("uagv/v2/Acme/SN-001/visualization", topic);
    }

    [Fact]
    public void AllStates_ReturnsWildcardPattern()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.AllStates(opts);

        Assert.Equal("uagv/v2/+/+/state", topic);
    }

    [Fact]
    public void AllConnections_ReturnsWildcardPattern()
    {
        var opts  = DefaultOptions();
        var topic = Vda5050Topic.AllConnections(opts);

        Assert.Equal("uagv/v2/+/+/connection", topic);
    }

    [Fact]
    public void Build_RespectsCustomInterfaceNameAndVersion()
    {
        var opts = new MqttOptions { InterfaceName = "myfleet", MajorVersion = "v3" };

        var topic = Vda5050Topic.Build(opts, "Acme", "SN-001", "state");

        Assert.Equal("myfleet/v3/Acme/SN-001/state", topic);
    }
}
