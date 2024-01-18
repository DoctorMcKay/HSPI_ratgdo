using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects.RpcSettings;

[JsonObject]
public class SaveConfigRequest {
	[JsonProperty("mqtt_host")] public string? MqttHost;
	[JsonProperty("mqtt_port")] public ushort? MqttPort;
	[JsonProperty("mqtt_username")] public string? MqttUsername;
	[JsonProperty("mqtt_password")] public string? MqttPassword;
}
