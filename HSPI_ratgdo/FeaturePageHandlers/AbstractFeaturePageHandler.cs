using System;
using System.Collections.Generic;
using HSPI_ratgdo.Enums;
using HSPI_ratgdo.JsonObjects;
using Newtonsoft.Json;

namespace HSPI_ratgdo.FeaturePageHandlers;

public abstract class AbstractFeaturePageHandler(HSPI plugin) {
	protected readonly HSPI Plugin = plugin;
	
	private readonly Dictionary<string, object> _results = new Dictionary<string, object>();
	
	private static readonly Dictionary<string, AbstractFeaturePageHandler> Handlers = new Dictionary<string, AbstractFeaturePageHandler>();

	public static AbstractFeaturePageHandler GetHandler(HSPI plugin, string page) {
		if (!Handlers.ContainsKey(page)) {
			switch (page) {
				case "settings.html":
					Handlers.Add(page, new Settings(plugin));
					break;
				
				default:
					throw new Exception($"Unknown page {page}");
			}
		}

		return Handlers[page];
	}

	public string PostBackProc(string data, string user) {
		JsonRpcRequest? req = JsonConvert.DeserializeObject<JsonRpcRequest>(data);
		if (req == null) {
			return JsonConvert.SerializeObject(
				new JsonRpcResponse(null, (int) RpcErrorCode.ParseError, "Parse error")
			);
		}

		if (req.Method == null) {
			return JsonConvert.SerializeObject(
				new JsonRpcResponse(req.Id, (int) RpcErrorCode.MethodNotFound, "Invalid method")
			);
		}

		if (req.Method.EndsWith(".Result")) {
			return JsonConvert.SerializeObject(ReturnResult(req));
		}

		if (_results.ContainsKey(req.Method)) {
			_results.Remove(req.Method);
		}

		return JsonConvert.SerializeObject(HandleCommand(req));
	}

	protected abstract JsonRpcResponse HandleCommand(JsonRpcRequest request);

	protected void SetResult(string method, object result) {
		_results[method] = result;
	}
	
	private JsonRpcResponse ReturnResult(JsonRpcRequest request) {
		string resultType = request.Method!.Substring(0, request.Method.Length - ".Result".Length);
		
		if (!_results.ContainsKey(resultType) || _results[resultType] == null) {
			return new JsonRpcResponse(request.Id, (int) RpcErrorCode.InvalidRequest, "Result not found");
		}

		return new JsonRpcResponse(request.Id, _results[resultType]);
	}
}
