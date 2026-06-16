using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DvMod.RemoteDispatch
{
	internal static class SignalsShim
	{
		// Cache method infos for the API methods we want to call, so we don't have to do reflection every time.
		private static MethodInfo? _teardownMethod;
		private static MethodInfo? _getAllSignalsMethod;
		private static MethodInfo? _getSignalAspectMethod;
		private static MethodInfo? _setSignalAspectMethod;
		// SignalMode is a .NET enum (Manual/Automatic), need it for API invocation
		// Cache reflection info to avoid recompiling every call
		private static MethodInfo? _setSignalModeMethod;

		internal static bool IsInitialized { get; private set; }

		/// <summary>
		/// Sets up the integration with the Signals mod if it's present and enabled.
		/// This should be called during the main mod's initialization.
		/// </summary>
		internal static void Initialize()
		{
			if (IsInitialized) return;

			// Check if Signals mod is present and enabled
			var signalsMod = UnityModManagerNet.UnityModManager.FindMod("DVSignals");

			Assembly? signalsAssembly = null;
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					if (asm.GetName().Name == "Signals.API")
					{
						signalsAssembly = asm;
						break;
					}
				}
				catch (Exception ex)
				{
					Main.DebugLog($"Error checking assembly '{asm.GetName().Name}': {ex.Message}");
				}
			}

			try
			{
				if (signalsMod == null || !signalsMod.Enabled)
				{
					Main.Warning("Signals mod not found or not enabled, signal integration disabled.");
					return;
				}

				if (signalsAssembly == null)
				{
					Main.Warning("Signals mod is enabled but Signals.API assembly not loaded.");
					return;
				}

				var path = Path.Combine(Main.mod!.Path, "RemoteDispatch.Signals.dll");

				if (!File.Exists(path))
				{
					Main.Warning("RemoteDispatch.Signals.dll not found, signal integration disabled.");
					return;
				}

				var integrationAssembly = Assembly.LoadFile(path);
				var bootstrap = integrationAssembly.GetType("DvMod.RemoteDispatch.Signals.Bootstrap");

				if (bootstrap == null)
				{
					Main.Warning("Failed to find DvMod.RemoteDispatch.Signals.Bootstrap, signal integration disabled.");
					return;
				}

				var initMethod = bootstrap.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
				// Load reflection-wrapped methods from the Bootstrap class in RemoteDispatch.Signals
				// This preserves the dependency chain: SignalsAPI → RemoteDispatch.Signals → RemoteDispatch.Shims
				_teardownMethod = bootstrap.GetMethod("Teardown", BindingFlags.Public | BindingFlags.Static);
				_getAllSignalsMethod = bootstrap.GetMethod("GetAllSignals", BindingFlags.Public | BindingFlags.Static);
				_getSignalAspectMethod = bootstrap.GetMethod("GetSignalAspect", BindingFlags.Public | BindingFlags.Static);
				_setSignalAspectMethod = bootstrap.GetMethod("SetSignalAspect", BindingFlags.Public | BindingFlags.Static);

				// Retrieve SetSignalMode method from Bootstrap (in RemoteDispatch.Signals DLL)
				// This maintains the same dependency chain as SetSignalAspect:
				// Signals.API → RemoteDispatch.Signals (bridge logic) → RemoteDispatch.Shims (reflection wrapper)
				_setSignalModeMethod = bootstrap.GetMethod("SetSignalMode", BindingFlags.Public | BindingFlags.Static);

				initMethod?.Invoke(null, new object[]
				{
					new Action<string>(msg => Main.Log(msg)),
					new Action<string>(msg => Main.DebugLog(msg)),
					new Action<string>(msg => Main.Warning(msg)),
					new Action<string, string>((signalId, aspect) => {
						Main.DebugLog($"Signals Action triggered: Aspect changed: {signalId} -> {aspect}, pushing update");
						Sessions.AddTag("signals");
						// Distant signals upstream of this one update automatically in-game, but their
						// SignalAspectChanged event may fire after the client already consumed the first
						// "signals" tag and took a snapshot - leaving the Distant icon stale with no
						// follow-up push ever arriving. Schedule a second push so any cascaded Distant
						// signal change always reaches connected clients.
						System.Threading.Tasks.Task.Delay(200).ContinueWith(_ => Sessions.AddTag("signals"));
					}),
					new Action<string, string>((signalId, mode) => {
						Main.DebugLog($"Signals Action triggered: Mode changed: {signalId} -> {mode}, pushing update");
						Sessions.AddTag("signals");
					}),

				});

				IsInitialized = true;
				Main.Log("Signals integration loaded.");
			}
			catch (Exception ex)
			{
				Main.Warning($"Failed to load Signals integration.\r\n{ex.Message}\r\n{ex.StackTrace}");
			}
		}

		/// <summary>
		/// Passes through the teardown call to the Signals integration, if it was initialized. This should be called during the main mod's shutdown to clean up any handlers in the Signals mod.
		/// </summary>
		internal static void Teardown()
		{
			if (!IsInitialized) return;

			try
			{
				// Do the teardown in a try/catch to avoid any exceptions from preventing the rest of the mod from unloading properly.
				_teardownMethod?.Invoke(null, null);
			}
			catch (Exception ex)
			{
				Main.Warning($"Failed to teardown Signals integration.\r\n{ex.Message}\r\n{ex.StackTrace}");
			}

			_teardownMethod = null;
			IsInitialized = false;
		}

		/// <summary>
		/// Returns all signals with their positions converted to lat/lng and adjusted for the WorldMover offset.
		/// If the Signals mod is not present or there is no signals data available, returns an empty JObject.
		/// </summary>
		/// <returns></returns>
		public static JToken? GetAllSignalsData()
		{
			Main.DebugLog("Getting all signals data...");
			var signalsData = _getAllSignalsMethod?.Invoke(null, null);

			if (signalsData is Dictionary<string, object> data)
			{
				var minimalData = SignalsShimHelpers.MinimalSignalDataProjection.Create(data);

				var jMinimalData = JObject.FromObject(minimalData);
				//Main.DebugLog($"SignalsShim.GetAllSignalsData returned: {JsonConvert.SerializeObject(jMinimalData).Replace('\n', ' ').Replace('\r', ' ')}");
				return jMinimalData;
			}

			Main.DebugLog("Signals mod not available or no signals data");
			return new JObject();
		}

		internal static string? GetSignalAspect(string signalId) =>
			_getSignalAspectMethod?.Invoke(null, new object[] { SignalsShimHelpers.StripSignalPrefix(signalId) }) as string;

		internal static bool SetSignalAspect(string signalId, string aspect) =>
			_setSignalAspectMethod?.Invoke(null, new object[] { SignalsShimHelpers.StripSignalPrefix(signalId), aspect }) is true;

		// Wrapper to expose Signals.API's SetSignalMode through reflection.
		// Accepts string mode (e.g. "Manual" or "Automatic"), forwards to RemoteDispatch.Signals.SignalsBridge.SetSignalMode().
		// Returns true on success, false on failure or 404 from API side.
		internal static bool SetSignalMode(string signalId, string mode) =>
			_setSignalModeMethod?.Invoke(null, new object[] { SignalsShimHelpers.StripSignalPrefix(signalId), mode }) is true;
	}
}
