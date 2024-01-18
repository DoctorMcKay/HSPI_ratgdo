using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects.RpcSettings;

[JsonObject]
public class GetMqttStatusResponse(string? status) {
	[JsonProperty("mqtt_status")] public string? MqttStatus = status;
}
