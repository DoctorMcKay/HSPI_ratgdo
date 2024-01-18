using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects.RpcSettings;

[JsonObject]
public class GetInfoResponse(string customSystemId, bool debugLogging) {
	[JsonProperty("custom_system_id")] public string CustomSystemId = customSystemId;
	[JsonProperty("debug_logging")] public bool DebugLogging = debugLogging;
	
	#if DEBUG
		[JsonProperty("debug_build")] public bool DebugBuild = true;
	#else
		[JsonProperty("debug_build")] public bool DebugBuild = false;
	#endif
}
