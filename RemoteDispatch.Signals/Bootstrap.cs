using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DvMod.RemoteDispatch.Signals
{
	public static class Bootstrap
	{
		private static SignalsBridge? _bridge;

		// Expose methods for the main mod to interact with signals without needing to reference SignalsAPI directly.
		public static Dictionary<string, object>? GetAllSignals() => _bridge?.GetAllSignals();
		public static string? GetSignalAspect(string signalId) => _bridge?.GetSignalAspect(signalId);
		public static bool SetSignalAspect(string signalId, string aspect) => _bridge?.SetSignalAspect(signalId, aspect) ?? false;
		public static bool SetSignalMode(string signalId, string mode) => _bridge?.SetSignalMode(signalId, mode) ?? false;

		/// <summary>
		/// Initialize the bridge and set up logging. This should be called by the main mod during its initialization.
		/// </summary>
		/// <param name="log"></param>
		/// <param name="debugLog"></param>
		/// <param name="warning"></param>
		/// <param name="onAspectChanged">Callback for aspect changes</param>
		/// <param name="onModeChanged">Callback for mode changes</param>
		public static void Initialize(Action<string> log, Action<string> debugLog, Action<string> warning, Action<string, string>? onAspectChanged = null, Action<string, string>? onModeChanged = null)
		{
			LoggingReturn.Initialize(log, debugLog, warning);
			try
			{
				_bridge = new SignalsBridge(onAspectChanged, onModeChanged);
				_bridge.Register();
				LoggingReturn.Log?.Invoke("Signals bridge initialized.");
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"Signals Initialize failed: {ex.Message}\n{ex.StackTrace}");
			}
		}

		///<summary>
		///Clean up the bridge and handlers. This should be called by the main mod during its shutdown.
		///
		///</summary>
		public static void Teardown()
		{
			try
			{
				_bridge?.Unregister();
				_bridge = null;
				LoggingReturn.Log?.Invoke("Signals bridge torn down.");
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"Teardown failed: {ex.Message}\n{ex.StackTrace}");
			}
		}
	}
}
