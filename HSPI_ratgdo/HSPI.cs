using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Controls;
using HomeSeer.PluginSdk.Logging;
using HSPI_ratgdo.Enums;
using HSPI_ratgdo.FeaturePageHandlers;
using HSPI_ratgdo.JsonObjects;
using MQTTnet.Client;
using Newtonsoft.Json;

namespace HSPI_ratgdo;

// ReSharper disable once InconsistentNaming
public class HSPI : AbstractPlugin {
	public const string PLUGIN_ID = "ratgdo";
	public const string PLUGIN_NAME = "ratgdo";

	public override string Id { get; } = PLUGIN_ID;
	public override string Name { get; } = PLUGIN_NAME;
	protected override string SettingsFileName { get; } = "ragdo.ini";

	private MqttClientManager? _mqttClient = null;
	private MqttServerManager? _mqttServer = null;

	private readonly Dictionary<string, RatgdoInstance> _ratgdoInstances = new Dictionary<string, RatgdoInstance>();
	
	private readonly AnalyticsClient _analyticsClient;

	public HSPI() {
		_analyticsClient = new AnalyticsClient(this, HomeSeerSystem);
	}
	
	protected override void Initialize() {
		WriteLog(ELogType.Trace, "Initialize");
		
		HomeSeerSystem.RegisterFeaturePage(Id, "settings.html", "Settings");
		
		// Prepare our ratgdo instances
		List<HsDevice> allDevices = HomeSeerSystem.GetAllDevices(false);
		foreach (HsDevice device in allDevices) {
			if (device.Interface != PLUGIN_ID || !device.Address.StartsWith(PLUGIN_ID)) {
				continue;
			}

			string[] addressParts = device.Address.Split(':');
			string doorName = addressParts[1];

			if (!_ratgdoInstances.ContainsKey(doorName)) {
				_ratgdoInstances[doorName] = new RatgdoInstance(this, doorName);
			}
		}
		
		// Check if we have a password generated for the internal broker
		string internalPassword = GetIniSetting("MQTT_Internal", "password", "");
		if (string.IsNullOrEmpty(internalPassword)) {
			internalPassword = Helpers.RandomString(16);
			SaveIniSetting("MQTT_Internal", "password", internalPassword);
		}

		MqttConnect().ContinueWith(_ => { });
	}

	protected override bool OnSettingChange(string pageId, AbstractView currentView, AbstractView changedView) {
		return false;
	}

	protected override void BeforeReturnStatus() {}

	public override string PostBackProc(string page, string data, string user, int userRights) {
		if ((userRights & 2) != 2) {
			return JsonConvert.SerializeObject(new JsonRpcResponse(null, 0, "Access Denied"));
		}

		try {
			AbstractFeaturePageHandler handler = AbstractFeaturePageHandler.GetHandler(this, page);
			return handler.PostBackProc(data, user);
		} catch (Exception ex) {
			WriteLog(ELogType.Warning, $"PostBackProc error for page {page}: {ex.Message}");
			return ex.Message;
		}
	}

	public override void SetIOMulti(List<ControlEvent> colSend) {
		foreach (ControlEvent ctrl in colSend) {
			HsFeature feature = HomeSeerSystem.GetFeatureByRef(ctrl.TargetRef);
			string[] addressParts = feature.Address.Split(':');
			string doorName = addressParts[1];

			if (!_ratgdoInstances.ContainsKey(doorName)) {
				WriteLog(ELogType.Error, $"Got SetIOMulti {ctrl.TargetRef} = {ctrl.ControlValue}, but no such ratgdo instance \"{doorName}\" found");
				continue;
			}

			RatgdoInstance instance = _ratgdoInstances[doorName];

			if (ctrl.TargetRef == instance.DoorFeatureRef) {
				DoorState requestedState = (DoorState) ctrl.ControlValue;
				instance.OperateDoor(requestedState).ContinueWith(_ => { });
			}

			if (ctrl.TargetRef == instance.LightFeatureRef) {
				BinaryState requestedState = (BinaryState) ctrl.ControlValue;
				instance.OperateBinary(RatgdoTopic.Light, requestedState).ContinueWith(_ => { });
			}
			
			if (ctrl.TargetRef == instance.LockFeatureRef) {
				BinaryState requestedState = (BinaryState) ctrl.ControlValue;
				instance.OperateBinary(RatgdoTopic.Lock, requestedState).ContinueWith(_ => { });
			}
		}
	}
	
	public IHsController GetHsController() {
		return HomeSeerSystem;
	}

	public string GetIniSetting(string section, string key, string defaultValue) {
		return HomeSeerSystem.GetINISetting(section, key, defaultValue, SettingsFileName);
	}

	public void SaveIniSetting(string section, string key, string value) {
		HomeSeerSystem.SaveINISetting(section, key, value, SettingsFileName);
	}

	public async Task MqttConnect() {
		string brokerAddress = GetIniSetting("MQTT", "broker_address", "internal");
		
		_mqttClient?.Dispose();
		_mqttClient = new MqttClientManager(this);

		foreach (RatgdoInstance instance in _ratgdoInstances.Values) {
			// Everything is offline when we're first connecting. If we don't update this here, we might never update it
			// if the device is truly offline, since we'd never get the Availability change if we're using the internal broker.
			// The internal broker doesn't save any persistent messages across plugin restarts.
			instance.HandleChange(RatgdoTopic.Availability, "offline", true);
		}

		if (brokerAddress == "internal") {
			if (_mqttServer == null) {
				WriteLog(ELogType.Debug, "Starting internal MQTT broker");
				_mqttServer = new MqttServerManager(
					this,
					PLUGIN_ID,
					GetIniSetting("MQTT_Internal", "password", "")
				);
				
				await _mqttServer.Initialize();
			}

			await _mqttClient.InternalConnect(_mqttServer);
		} else {
			ushort brokerPort = ushort.Parse(GetIniSetting("MQTT", "broker_port", "1883"));
			string username = GetIniSetting("MQTT", "client_username", "");
			string password = GetIniSetting("MQTT", "client_password", "");

			await _mqttClient.ExternalConnect(brokerAddress, brokerPort, username, password);
			
			// Stop our internal broker if we have one
			if (_mqttServer != null) {
				WriteLog(ELogType.Debug, "Disposing internal MQTT broker since we're connecting to an external one");
				_mqttServer.Dispose();
				_mqttServer = null;
			}
		}

		_mqttClient.OnRatgdoEvent += (object src, MqttClientManager.RatgdoEvent args) => {
			bool isDoorRecognized = _ratgdoInstances.ContainsKey(args.DoorName);

			if (!isDoorRecognized && (args.Topic != RatgdoTopic.Availability || args.Value != "online")) {
				// We only want to create new doors if they're online. Otherwise, we might create an HS4 device for
				// a ratgdo that's had its name changed, but the keys remain in MQTT under the old name due to persistence.
				WriteLog(ELogType.Debug, $"Ignoring {args.Topic} = {args.Value} for unknown door {args.DoorName}");
				return;
			}

			if (!isDoorRecognized) {
				_ratgdoInstances[args.DoorName] = new RatgdoInstance(this, args.DoorName);
			}
			
			_ratgdoInstances[args.DoorName].HandleChange(args.Topic, args.Value);
		};
	}

	public async Task MqttSend(string topic, string payload) {
		if (_mqttClient != null) {
			await _mqttClient.Publish(topic, payload);
		}
	}

	public RatgdoInstance[] GetRatgdoInstances() {
		return _ratgdoInstances.Values.ToArray();
	}

	public string? GetMqttStatus() {
		MqttClientConnectResultCode? code = _mqttClient?.ConnectedStatus;
		if (code == null) {
			return null;
		}

		return Enum.GetName(typeof(MqttClientConnectResultCode), code);
	}
	
	public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string? caller = null) {
		_analyticsClient.WriteLog(logType, message, lineNumber, caller);
		
		#if DEBUG
			bool isDebugMode = true;

			// Prepend calling function and line number
			message = $"[{caller}:{lineNumber}] {message}";
				
			// Also print to console in debug builds
			string type = logType.ToString().ToLower();
			Console.WriteLine($"[{type}] {message}");
		#else
			bool isDebugMode = _debugLogging;
		#endif

		if (logType <= ELogType.Debug && !isDebugMode) {
			return;
		}
			
		HomeSeerSystem.WriteLog(logType, message, Name);
	}
}
