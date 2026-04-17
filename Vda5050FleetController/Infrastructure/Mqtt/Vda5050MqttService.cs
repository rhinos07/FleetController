using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vda5050FleetController.Domain.Models;

namespace Vda5050FleetController.Infrastructure.Mqtt;

// ── Configuration ─────────────────────────────────────────────────────────────

public class MqttOptions
{
    public string  Host          { get; set; } = "localhost";
    public int     Port          { get; set; } = 1883;
    public string  ClientId      { get; set; } = "fleet-controller";
    public string  InterfaceName { get; set; } = "uagv";
    public string  MajorVersion  { get; set; } = "v2";
}

// ── Topic helper ──────────────────────────────────────────────────────────────

public static class Vda5050Topic
{
    public static string Build(MqttOptions opts, string manufacturer, string serial, string topic)
        => $"{opts.InterfaceName}/{opts.MajorVersion}/{manufacturer}/{serial}/{topic}";

    public static string Order        (MqttOptions o, string m, string s) => Build(o, m, s, "order");
    public static string InstantAction(MqttOptions o, string m, string s) => Build(o, m, s, "instantActions");
    public static string State        (MqttOptions o, string m, string s) => Build(o, m, s, "state");
    public static string Connection   (MqttOptions o, string m, string s) => Build(o, m, s, "connection");
    public static string Visualization(MqttOptions o, string m, string s) => Build(o, m, s, "visualization");

    // Wildcard subscription: all vehicles
    public static string AllStates     (MqttOptions o) => $"{o.InterfaceName}/{o.MajorVersion}/+/+/state";
    public static string AllConnections(MqttOptions o) => $"{o.InterfaceName}/{o.MajorVersion}/+/+/connection";
}

// ── MQTT Service ──────────────────────────────────────────────────────────────

public interface IVda5050MqttService
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    Task PublishOrderAsync(Order order, CancellationToken ct = default);
    Task PublishInstantActionAsync(InstantActions actions, CancellationToken ct = default);

    event Func<VehicleState,      Task> OnStateReceived;
    event Func<ConnectionMessage, Task> OnConnectionReceived;
}

public class Vda5050MqttService : IVda5050MqttService, IAsyncDisposable
{
    private readonly IMqttClient    _client;
    private readonly MqttOptions    _opts;
    private readonly ILogger<Vda5050MqttService> _log;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false
    };

    public event Func<VehicleState,      Task> OnStateReceived      = _ => Task.CompletedTask;
    public event Func<ConnectionMessage, Task> OnConnectionReceived  = _ => Task.CompletedTask;

    public Vda5050MqttService(IOptions<MqttOptions> opts, ILogger<Vda5050MqttService> log)
    {
        _opts   = opts.Value;
        _log    = log;
        _client = new MqttFactory().CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_opts.Host, _opts.Port)
            .WithClientId(_opts.ClientId)
            .WithCleanSession(true)
            .Build();

        _log.LogInformation("Connecting to MQTT broker {Host}:{Port}", _opts.Host, _opts.Port);
        await _client.ConnectAsync(options, ct);

        // Subscribe to all vehicle state + connection topics
        await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(Vda5050Topic.AllStates(_opts),      MqttQualityOfServiceLevel.AtMostOnce)
            .WithTopicFilter(Vda5050Topic.AllConnections(_opts),  MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        _log.LogInformation("MQTT connected and subscribed");
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
        => await _client.DisconnectAsync(cancellationToken: ct);

    public Task PublishOrderAsync(Order order, CancellationToken ct = default)
    {
        var topic   = Vda5050Topic.Order(_opts, order.Manufacturer, order.SerialNumber);
        var payload = JsonSerializer.Serialize(order, _json);

        _log.LogDebug("Publishing Order {OrderId} to {Topic}", order.OrderId, topic);
        return PublishAsync(topic, payload, MqttQualityOfServiceLevel.AtLeastOnce, ct);
    }

    public Task PublishInstantActionAsync(InstantActions actions, CancellationToken ct = default)
    {
        var topic   = Vda5050Topic.InstantAction(_opts, actions.Manufacturer, actions.SerialNumber);
        var payload = JsonSerializer.Serialize(actions, _json);

        _log.LogDebug("Publishing InstantAction to {Topic}", topic);
        return PublishAsync(topic, payload, MqttQualityOfServiceLevel.AtLeastOnce, ct);
    }

    private Task PublishAsync(string topic, string payload,
        MqttQualityOfServiceLevel qos, CancellationToken ct)
    {
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(false)
            .Build();

        return _client.PublishAsync(msg, ct);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic   = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

        try
        {
            if (topic.EndsWith("/state"))
            {
                var state = JsonSerializer.Deserialize<VehicleState>(payload, _json);
                if (state is not null) await OnStateReceived(state);
            }
            else if (topic.EndsWith("/connection"))
            {
                var conn = JsonSerializer.Deserialize<ConnectionMessage>(payload, _json);
                if (conn is not null) await OnConnectionReceived(conn);
            }
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to deserialize message on topic {Topic}", topic);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync();
        _client.Dispose();
    }
}
