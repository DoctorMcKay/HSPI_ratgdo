using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Logging;
using HSPI_ratgdo.Enums;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace HSPI_ratgdo;

public class MqttClientManager : IDisposable {
	public event EventHandler<RatgdoEvent>? OnRatgdoEvent;

	public MqttClientConnectResultCode? ConnectedStatus { get; private set; } = null;
	
	private readonly HSPI _plugin;
	private readonly IManagedMqttClient _client;

	#if DEBUG
		public const string MQTT_CLIENT_ID = $"HSPI_{HSPI.PLUGIN_ID}_DEBUG";
	#else
		public const string MQTT_CLIENT_ID = $"HSPI_{HSPI.PLUGIN_ID}";
	#endif

	public MqttClientManager(HSPI plugin) {
		_plugin = plugin;
		_client = new MqttFactory().CreateManagedMqttClient();

		_client.ConnectedAsync += (MqttClientConnectedEventArgs args) => {
			ConnectedStatus = args.ConnectResult.ResultCode;
			_plugin.WriteLog(ELogType.Debug, $"Now connected to MQTT broker: {ConnectedStatus}");
			return Task.CompletedTask;
		};

		_client.DisconnectedAsync += (MqttClientDisconnectedEventArgs args) => {
			ConnectedStatus = args.ConnectResult?.ResultCode;
			string logReason = ConnectedStatus?.ToString() ?? "Connection Rejected";
			_plugin.WriteLog(ELogType.Warning, $"Disconnected from MQTT broker: {logReason}");
			return Task.CompletedTask;
		};
	}

	public async Task InternalConnect(MqttServerManager serverManager) {
		ManagedMqttClientOptions options = new ManagedMqttClientOptionsBuilder()
			.WithClientOptions(serverManager.GetClientOptions())
			.Build();

		await _client.StartAsync(options);
		await _subscribe();
	}

	public async Task ExternalConnect(string address, ushort port, string username, string password) {
		MqttClientOptions options = new MqttClientOptionsBuilder()
			.WithTcpServer(address, port)
			.WithCredentials(username, password)
			.WithClientId(MQTT_CLIENT_ID)
			.Build();

		ManagedMqttClientOptions managedOptions = new ManagedMqttClientOptionsBuilder()
			.WithClientOptions(options)
			.Build();

		await _client.StartAsync(managedOptions);
		await _subscribe();
	}

	public async Task Publish(string topic, string payload) {
		await _client.EnqueueAsync(topic, payload);
	}

	private async Task _subscribe() {
		await _client.SubscribeAsync("ratgdo/#");

		_client.ApplicationMessageReceivedAsync += (MqttApplicationMessageReceivedEventArgs args) => {
			string pattern = "^ratgdo/([^/]+)/status/([^/]+)$";
			Match match = Regex.Match(args.ApplicationMessage.Topic, pattern, RegexOptions.None);
			if (!match.Success) {
				return Task.CompletedTask;
			}

			if (!Enum.TryParse(match.Groups[2].ToString(), true, out RatgdoTopic ratgdoTopic)) {
				_plugin.WriteLog(ELogType.Debug, $"Unrecognized ratgdo topic \"{match.Groups[2]}\"");
				return Task.CompletedTask;
			}

			string doorName = match.Groups[1].ToString();
			string content = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment.ToArray());
			_plugin.WriteLog(ELogType.Debug, $"[{doorName}] {ratgdoTopic}: {content}");
			
			OnRatgdoEvent?.Invoke(this, new RatgdoEvent(doorName, ratgdoTopic, content));

			return Task.CompletedTask;
		};
	}

	public void Dispose() {
		_client.Dispose();
	}

	public class RatgdoEvent : EventArgs {
		internal RatgdoEvent(string doorName, RatgdoTopic topic, string value) {
			DoorName = doorName;
			Topic = topic;
			Value = value;
		}
		
		public string DoorName;
		public RatgdoTopic Topic;
		public string Value;
	}
}
