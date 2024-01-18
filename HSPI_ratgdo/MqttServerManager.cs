using System;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace HSPI_ratgdo;

public class MqttServerManager : IDisposable {
	private readonly HSPI _plugin;
	private readonly MqttServer _server;

	private readonly string _clientUsername;
	private readonly string _clientPassword;
	
	private string _internalClientPassword = "";

	public const ushort MQTT_PORT = 18120;

	public MqttServerManager(HSPI plugin, string clientUsername, string clientPassword) {
		MqttServerOptions options = new MqttServerOptionsBuilder()
			.WithDefaultEndpoint()
			.WithDefaultEndpointPort(MQTT_PORT)
			.Build();

		_plugin = plugin;
		_server = new MqttFactory().CreateMqttServer(options);

		_clientUsername = clientUsername;
		_clientPassword = clientPassword;
	}

	public async Task Initialize() {
		_server.ValidatingConnectionAsync += (ValidatingConnectionEventArgs args) => {
			_plugin.WriteLog(ELogType.Debug, $"Incoming MQTT connection from {args.ClientId} ({args.UserName})");

			if (args.UserName == MqttClientManager.MQTT_CLIENT_ID) {
				// This is a connection from our own internal client.
				if (args.ClientId != args.UserName) {
					args.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
				}

				if (
					string.IsNullOrEmpty(_internalClientPassword)
					|| _internalClientPassword != args.Password
					|| DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - long.Parse(_internalClientPassword) > 5000
				) {
					args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
				}
			} else {
				// This should be a connection from a ratgdo, which means the credentials must match.
				if (args.Password != _clientPassword) {
					args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
				}
			}

			if (args.ReasonCode != MqttConnectReasonCode.Success) {
				_plugin.WriteLog(ELogType.Warning, $"Invalid MQTT connection attempt from {args.Endpoint} with username \"{args.UserName}\"");
			}
			
			return Task.CompletedTask;
		};
		
		_server.InterceptingPublishAsync += (InterceptingPublishEventArgs args) => {
			_plugin.WriteLog(ELogType.Trace, $"MQTT: Message published on topic \"{args.ApplicationMessage.Topic}\" by \"{args.ClientId}\"");
			return Task.CompletedTask;
		};
		
		await _server.StartAsync();
	}

	public MqttClientOptions GetClientOptions() {
		_internalClientPassword = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

		return new MqttClientOptionsBuilder()
			.WithClientId(MqttClientManager.MQTT_CLIENT_ID)
			.WithCredentials(MqttClientManager.MQTT_CLIENT_ID, _internalClientPassword)
			.WithTcpServer("localhost", MQTT_PORT)
			.Build();
	}

	public void Dispose() {
		_server.Dispose();
	}
}
