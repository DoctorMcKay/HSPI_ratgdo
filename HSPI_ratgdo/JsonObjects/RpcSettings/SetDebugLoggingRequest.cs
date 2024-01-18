using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects.RpcSettings;

[JsonObject]
public class SetDebugLoggingRequest {
	[JsonProperty("debug_logging")] public bool? DebugLogging;
}
