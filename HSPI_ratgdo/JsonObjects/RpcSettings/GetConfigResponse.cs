using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects.RpcSettings;

[JsonObject]
public class GetConfigResponse {
	[JsonProperty("mqtt_host")] public string? MqttHost;
	[JsonProperty("mqtt_port")] public ushort? MqttPort;
	[JsonProperty("mqtt_username")] public string? MqttUsername;
	[JsonProperty("mqtt_password")] public string? MqttPassword;
	
	[JsonProperty("local_address")] public string? LocalAddress;
	[JsonProperty("mqtt_internal_broker_username")] public string? MqttInternalBrokerUsername;
	[JsonProperty("mqtt_internal_broker_password")] public string? MqttInternalBrokerPassword;
}
