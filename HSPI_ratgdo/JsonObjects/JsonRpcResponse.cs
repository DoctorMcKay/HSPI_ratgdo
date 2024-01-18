using Newtonsoft.Json;

namespace HSPI_ratgdo.JsonObjects;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class JsonRpcResponse {
	[JsonProperty("jsonrpc")] public string JsonRpc = "2.0";
	[JsonProperty("id")] public object? Id;
	
	[JsonProperty("result")] public object? Result;
	[JsonProperty("error")] public RpcError? Error;

	public JsonRpcResponse(object? id, object? result) {
		Id = id;
		Result = result;
	}

	public JsonRpcResponse(object? id, int errorCode, string errorMessage, object? errorData = null) {
		Id = id;
		Error = new RpcError(errorCode, errorMessage, errorData);
	}
	
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class RpcError(int code, string message, object? data = null) {
		[JsonProperty("code")] public int Code = code;
		[JsonProperty("message")] public string Message = message;
		[JsonProperty("data")] public object? Data = data;
	}
}
