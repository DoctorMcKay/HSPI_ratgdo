using System;
using System.Threading.Tasks;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Devices.Controls;
using HomeSeer.PluginSdk.Devices.Identification;
using HomeSeer.PluginSdk.Logging;
using HSPI_ratgdo.Enums;
using Newtonsoft.Json;

namespace HSPI_ratgdo;

[JsonObject]
public class RatgdoInstance {
	[JsonProperty("name")]
	public readonly string DoorName;

	[JsonProperty("is_online")]
	public bool IsOnline { get; private set; } = false;

	[JsonIgnore] public int RootDeviceRef { get; private set; }
	[JsonIgnore] public int DoorFeatureRef { get; private set; }
	[JsonIgnore] public int LightFeatureRef { get; private set; }
	[JsonIgnore] public int LockFeatureRef { get; private set; }
	[JsonIgnore] public int ObstructionFeatureRef { get; private set; }

	private readonly HSPI _plugin;

	private string RootAddress => $"ratgdo:{DoorName}";

	public RatgdoInstance(HSPI plugin, string doorName) {
		_plugin = plugin;
		DoorName = doorName;

		HsDevice? rootDevice = _plugin.GetHsController().GetDeviceByAddress(RootAddress);
		if (rootDevice == null) {
			_plugin.WriteLog(ELogType.Info, $"Creating HS4 device for newly discovered ratgdo \"{DoorName}\"");

			DeviceFactory deviceFactory = DeviceFactory.CreateDevice(HSPI.PLUGIN_ID)
				.AsType(EDeviceType.Door, 0)
				.WithAddress(RootAddress)
				.WithName(DoorName);

			RootDeviceRef = _plugin.GetHsController().CreateDevice(deviceFactory.PrepareForHs());
		} else {
			RootDeviceRef = rootDevice.Ref;
		}

		_findOrCreateDoorFeature();
		_findOrCreateLightFeature();
		_findOrCreateLockFeature();
		_findOrCreateObstructionFeature();
	}

	public void HandleChange(RatgdoTopic topic, string value, bool isStartingUp = false) {
		if (value == "unknown") {
			_plugin.WriteLog(ELogType.Debug, $"Dropping update with value \"unknown\" for topic \"{topic}\"");
			return;
		}

		if (topic != RatgdoTopic.Availability && !IsOnline) {
			// We can't trust any data if we haven't seen the ratgdo as online, since mqtt will save retained state
			// even if the ratgdo is offline.
			return;
		}
		
		switch (topic) {
			case RatgdoTopic.Availability:
				bool online = value == "online";
				IsOnline = online;
				
				if (online) {
					Task.Delay(10000).ContinueWith(_ => {
						_plugin.WriteLog(ELogType.Debug, $"Door \"{DoorName}\" is now online. Querying its status.");
						QueryStatus().ContinueWith(_ => { });
					});
				} else {
					if (!isStartingUp) {
						_plugin.WriteLog(ELogType.Warning, $"Door \"{DoorName}\" is now offline.");
					}

					_plugin.GetHsController().UpdateFeatureValueByRef(DoorFeatureRef, (int) DoorState.Offline);
				}

				break;
			
			case RatgdoTopic.Door:
				if (!Enum.TryParse(value, true, out DoorState doorState)) {
					_plugin.WriteLog(ELogType.Error, $"Door \"{DoorName}\" is in an unrecognized state: {value}");
					return;
				}

				_plugin.GetHsController().UpdateFeatureValueByRef(DoorFeatureRef, (int) doorState);
				break;
			
			case RatgdoTopic.Light:
			case RatgdoTopic.Lock:
			case RatgdoTopic.Obstruction:
				BinaryState binaryState;
				
				switch (value) {
					case "off":
					case "unlocked":
					case "clear":
						binaryState = BinaryState.OffUnlockedUnobstructed;
						break;
						
					case "on":
					case "locked":
					case "obstructed":
						binaryState = BinaryState.OnLockedObstructed;
						break;
					
					default:
						_plugin.WriteLog(ELogType.Error, $"Door \"{DoorName}\" feature {topic} is in an unrecognized state: {value}");
						return;
				}

				int featureRef;
				
				switch (topic) {
					case RatgdoTopic.Light:
						featureRef = LightFeatureRef;
						break;
					
					case RatgdoTopic.Lock:
						featureRef = LockFeatureRef;
						break;
					
					case RatgdoTopic.Obstruction:
						featureRef = ObstructionFeatureRef;
						break;
					
					default:
						throw new Exception("Something's broken real bad, man");
				}

				_plugin.GetHsController().UpdateFeatureValueByRef(featureRef, (int) binaryState);
				break;
		}
	}

	public async Task OperateDoor(DoorState requestedState) {
		if (requestedState != DoorState.Open && requestedState != DoorState.Closed) {
			throw new Exception($"Invalid requested door state {requestedState}");
		}

		await _plugin.MqttSend($"ratgdo/{DoorName}/command/door", requestedState == DoorState.Open ? "open" : "close");
	}

	public async Task OperateBinary(RatgdoTopic topic, BinaryState requestedState) {
		switch (topic) {
			case RatgdoTopic.Light:
				await _plugin.MqttSend($"ratgdo/{DoorName}/command/light", requestedState == BinaryState.OnLockedObstructed ? "on" : "off");
				break;
			
			case RatgdoTopic.Lock:
				await _plugin.MqttSend($"ratgdo/{DoorName}/command/lock", requestedState == BinaryState.OnLockedObstructed ? "lock" : "unlock");
				break;
			
			default:
				throw new Exception($"Cannot operate {topic} as a binary entity");
		}
	}

	public async Task QueryStatus() {
		await _plugin.MqttSend($"ratgdo/{DoorName}/command", "query");
	}

	private void _findOrCreateDoorFeature() {
		HsFeature? feature = _plugin.GetHsController().GetFeatureByAddress($"{RootAddress}:Door");
		if (feature == null) {
			FeatureFactory factory = FeatureFactory.CreateFeature(HSPI.PLUGIN_ID, RootDeviceRef)
				.AsType(EFeatureType.Security, (int) EGenericFeatureSubType.BinaryControl)
				.WithAddress($"{RootAddress}:Door")
				.WithName("Door")
				.WithDisplayType(EFeatureDisplayType.Important)
				.AddGraphicForValue("/images/HomeSeer/status/Garage-Closed.png", (int) DoorState.Closed, "Closed")
				.AddGraphicForValue("/images/HomeSeer/status/Garage-Closing.png", (int) DoorState.Closing, "Closing")
				.AddGraphicForValue("/images/HomeSeer/status/Garage-Open.png", (int) DoorState.Open, "Open")
				.AddGraphicForValue("/images/HomeSeer/status/Garage-Opening.png", (int) DoorState.Opening, "Opening")
				.AddGraphicForValue("/images/HomeSeer/status/Garage-Stopped.png", (int) DoorState.Stopped, "Stopped")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (int) DoorState.Offline, "Offline")
				.AddButton((int) DoorState.Closed, "Close", null, EControlUse.DoorLock)
				.AddButton((int) DoorState.Open, "Open", null, EControlUse.DoorUnLock);

			DoorFeatureRef = _plugin.GetHsController().CreateFeatureForDevice(factory.PrepareForHs());
		} else {
			DoorFeatureRef = feature.Ref;
		}
	}
	
	private void _findOrCreateLightFeature() {
		HsFeature? feature = _plugin.GetHsController().GetFeatureByAddress($"{RootAddress}:Light");
		if (feature == null) {
			FeatureFactory factory = FeatureFactory.CreateFeature(HSPI.PLUGIN_ID, RootDeviceRef)
				.AsType(EFeatureType.Generic, (int) EGenericFeatureSubType.BinaryControl)
				.WithAddress($"{RootAddress}:Light")
				.WithName("Light")
				.AddGraphicForValue("/images/HomeSeer/status/off.gif", (int) BinaryState.OffUnlockedUnobstructed, "Off")
				.AddGraphicForValue("/images/HomeSeer/status/on.gif", (int) BinaryState.OnLockedObstructed, "On")
				.AddButton((int) BinaryState.OffUnlockedUnobstructed, "Off", null, EControlUse.Off)
				.AddButton((int) BinaryState.OnLockedObstructed, "On", null, EControlUse.On);

			LightFeatureRef = _plugin.GetHsController().CreateFeatureForDevice(factory.PrepareForHs());
		} else {
			LightFeatureRef = feature.Ref;
		}
	}
	
	private void _findOrCreateLockFeature() {
		HsFeature? feature = _plugin.GetHsController().GetFeatureByAddress($"{RootAddress}:Lock");
		if (feature == null) {
			FeatureFactory factory = FeatureFactory.CreateFeature(HSPI.PLUGIN_ID, RootDeviceRef)
				.AsType(EFeatureType.Security, (int) EGenericFeatureSubType.BinaryControl)
				.WithAddress($"{RootAddress}:Lock")
				.WithName("Remote Lockout")
				.AddGraphicForValue("/images/HomeSeer/status/unlocked.gif", (int) BinaryState.OffUnlockedUnobstructed, "Unlocked")
				.AddGraphicForValue("/images/HomeSeer/status/locked.gif", (int) BinaryState.OnLockedObstructed, "Locked")
				.AddButton((int) BinaryState.OffUnlockedUnobstructed, "Unlock", null, EControlUse.DoorUnLock)
				.AddButton((int) BinaryState.OnLockedObstructed, "Lock", null, EControlUse.DoorLock);

			LockFeatureRef = _plugin.GetHsController().CreateFeatureForDevice(factory.PrepareForHs());
		} else {
			LockFeatureRef = feature.Ref;
		}
	}
	
	private void _findOrCreateObstructionFeature() {
		HsFeature? feature = _plugin.GetHsController().GetFeatureByAddress($"{RootAddress}:Obstruction");
		if (feature == null) {
			FeatureFactory factory = FeatureFactory.CreateFeature(HSPI.PLUGIN_ID, RootDeviceRef)
				.AsType(EFeatureType.Security, (int) EGenericFeatureSubType.BinaryControl)
				.WithAddress($"{RootAddress}:Obstruction")
				.WithName("Safety Beam")
				.AddGraphicForValue("/images/HomeSeer/status/ok.png", (int) BinaryState.OffUnlockedUnobstructed, "Clear")
				.AddGraphicForValue("/images/HomeSeer/status/alarm.png", (int) BinaryState.OnLockedObstructed, "Obstructed");

			ObstructionFeatureRef = _plugin.GetHsController().CreateFeatureForDevice(factory.PrepareForHs());
		} else {
			ObstructionFeatureRef = feature.Ref;
		}
	}
}
