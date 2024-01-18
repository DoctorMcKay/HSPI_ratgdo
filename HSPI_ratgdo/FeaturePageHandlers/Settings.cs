using System;
using System.Net;
using System.Net.Sockets;
using HSPI_ratgdo.Enums;
using HSPI_ratgdo.JsonObjects;
using HSPI_ratgdo.JsonObjects.RpcSettings;
using Newtonsoft.Json;

namespace HSPI_ratgdo.FeaturePageHandlers;

public class Settings(HSPI plugin) : AbstractFeaturePageHandler(plugin) {
	private static string? _localAddress = null;

	private static string _getLocalAddress() {
		if (_localAddress != null) {
			return _localAddress;
		}
		
		// This is a more reliable way of determining our actual LAN address than using NetworkInterface or Dns since
		// this will figure out what adapter is actually used to make a connection. We don't actually send traffic here.

		try {
			using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
			socket.Connect("8.8.8.8", 65530);
			IPEndPoint? endpoint = socket.LocalEndPoint as IPEndPoint;
			_localAddress = endpoint!.Address.ToString();
		} catch (SocketException) {
			return "127.0.0.1";
		}

		return _localAddress;
	}
	
	protected override JsonRpcResponse HandleCommand(JsonRpcRequest request) {
		switch (request.Method) {
			case "GetInfo":
				return _getInfo(request);
			
			case "SetDebugLogging":
				return _setDebugLogging(request);
			
			case "GetConfig":
				return _getConfig(request);
			
			case "GetDevices":
				return _getDevices(request);
			
			case "GetMqttStatus":
				return _getMqttStatus(request);
			
			case "SaveConfig":
				return _saveConfig(request);
			
			default:
				return new JsonRpcResponse(request.Id, (int) RpcErrorCode.MethodNotFound, "Method not found");
		}
	}

	private JsonRpcResponse _getInfo(JsonRpcRequest request) {
		return request.Respond(new GetInfoResponse(Plugin.CustomSystemId ?? "", Plugin.DebugLogging));
	}

	private JsonRpcResponse _setDebugLogging(JsonRpcRequest request) {
		SetDebugLoggingRequest? input = JsonConvert.DeserializeObject<SetDebugLoggingRequest>(JsonConvert.SerializeObject(request.Params));
		if (input?.DebugLogging == null) {
			request.Error((int) RpcErrorCode.InvalidParams, "Invalid params");
		}

		// We already checked for null above but C# is dumb
		Plugin.DebugLogging = input?.DebugLogging ?? false;

		return request.Respond("OK");
	}

	private JsonRpcResponse _getConfig(JsonRpcRequest request) {
		string brokerAddress = Plugin.GetIniSetting("MQTT", "broker_address", "internal");
		ushort brokerPort = brokerAddress == "internal"
			? MqttServerManager.MQTT_PORT
			: ushort.Parse(Plugin.GetIniSetting("MQTT", "broker_port", "1883"));
		
		GetConfigResponse response = new GetConfigResponse {
			MqttHost = brokerAddress,
			MqttPort = brokerPort,
			MqttUsername = Plugin.GetIniSetting("MQTT", "client_username", ""),
			MqttPassword = Plugin.GetIniSetting("MQTT", "client_password", ""),
			
			LocalAddress = _getLocalAddress(),
			MqttInternalBrokerUsername = HSPI.PLUGIN_ID,
			MqttInternalBrokerPassword = Plugin.GetIniSetting("MQTT_Internal", "password", "")
		};

		return request.Respond(response);
	}

	private JsonRpcResponse _getDevices(JsonRpcRequest request) {
		return request.Respond(Plugin.GetRatgdoInstances());
	}

	private JsonRpcResponse _getMqttStatus(JsonRpcRequest request) {
		return request.Respond(new GetMqttStatusResponse(Plugin.GetMqttStatus()));
	}

	private JsonRpcResponse _saveConfig(JsonRpcRequest request) {
		SaveConfigRequest? input = JsonConvert.DeserializeObject<SaveConfigRequest>(JsonConvert.SerializeObject(request.Params));

		if (input == null) {
			return request.Error((int) RpcErrorCode.InvalidParams, "Unable to parse params");
		}

		if (input.MqttHost == null) {
			return request.Error((int) RpcErrorCode.InvalidParams, "MQTT host must be provided");
		}

		if (input.MqttHost != "internal" && input.MqttPort == null) {
			return request.Error((int) RpcErrorCode.InvalidParams, "MQTT port must be provided");
		}
		
		Plugin.SaveIniSetting("MQTT", "broker_address", input.MqttHost);
		Plugin.SaveIniSetting("MQTT", "broker_port", input.MqttPort?.ToString() ?? "1883");
		Plugin.SaveIniSetting("MQTT", "client_username", input.MqttUsername ?? "");
		Plugin.SaveIniSetting("MQTT", "client_password", input.MqttPassword ?? "");

		Plugin.MqttConnect().ContinueWith(_ => { });

		return request.Respond("OK");
	}
}
