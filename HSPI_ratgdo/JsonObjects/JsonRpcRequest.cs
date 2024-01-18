using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects;

[JsonObject]
public class JsonRpcRequest(object id, string method, object methodParams) {
	[JsonProperty("jsonrpc")] public string JsonRpc = "2.0";
	[JsonProperty("id")] public object? Id = id;
	[JsonProperty("method")] public string? Method = method;
	[JsonProperty("params")] public object? Params = methodParams;

	public JsonRpcResponse Respond(object result) {
		return new JsonRpcResponse(Id, result);
	}

	public JsonRpcResponse Error(int code, string message, object? data = null) {
		return new JsonRpcResponse(Id, code, message, data);
	}
}
