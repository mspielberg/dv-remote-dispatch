using System;
using System.Collections.Generic;
using Signals.API;
using Newtonsoft.Json;

namespace DvMod.RemoteDispatch.Signals
{
	internal class SignalsBridge
	{
		/// <summary>Callbacks to invoke on event</summary>
		private readonly Action<string, string>? _onAspectChanged;
		private readonly Action<string, string>? _onModeChanged;

		/// <summary>Constructor - inject callbacks for forward compat</summary>
		internal SignalsBridge(Action<string, string>? onAspectChanged = null, Action<string, string>? onModeChanged = null)
		{
			_onAspectChanged = onAspectChanged;
			_onModeChanged = onModeChanged;
		}

		/// <summary>
		/// Registers event handlers for the SignalsAPI loaded and unloaded events.
		/// </summary>
		/// <remarks>Call this method to enable automatic handling of SignalsAPI lifecycle events within
		/// the current context. This method should be called only once per instance to avoid multiple
		/// registrations.</remarks>
		internal void Register()
		{
			// When SignalsAPI is Loaded, run OnSignalsLoaded
			SignalsAPI.Loaded += OnSignalsLoaded;
			// When SignalsAPI is Unloaded, run OnSignalsUnloaded
			SignalsAPI.Unloaded += OnSignalsUnloaded;

			// If SignalsAPI is already loaded before we registered, we missed the event.
			// Subscribe to instance events directly now.
			if (SignalsAPI.Instance != null)
			{
				OnSignalsLoaded();
			}
		}

		/// <summary>
		/// Unregisters event handlers from the SignalsAPI to stop receiving signal-related notifications.
		/// </summary>
		/// <remarks>Call this method to detach previously registered event handlers and prevent further
		/// callbacks from the SignalsAPI. This is typically used during cleanup to avoid memory leaks or unintended
		/// behavior after the object is no longer needed.</remarks>
		internal void Unregister()
		{
			// Make sure to undo everything we did in Register
			SignalsAPI.Loaded -= OnSignalsLoaded;
			SignalsAPI.Unloaded -= OnSignalsUnloaded;
			if (SignalsAPI.Instance != null)
			{
				// Also remove the handlers if the API is still around
				SignalsAPI.Instance.SignalAspectChanged -= OnAspectChanged;
				SignalsAPI.Instance.SignalModeChanged -= OnModeChanged;
			}
		}

		/// <summary>
		/// Initializes event handlers for signal aspect and mode changes after the Signals API has been loaded.
		/// </summary>
		private void OnSignalsLoaded()
		{
			SignalsAPI.Instance!.SignalAspectChanged += OnAspectChanged;
			SignalsAPI.Instance!.SignalModeChanged += OnModeChanged;
			LoggingReturn.DebugLog?.Invoke("Signals API loaded, ready to interact with signals.");
		}

		/// <summary>
		/// Handles cleanup operations when the Signals API is unloaded.
		/// </summary>
		private void OnSignalsUnloaded()
		{
			LoggingReturn.DebugLog?.Invoke("Signals API unloaded, cleaned up handlers.");
		}

		/// <summary>
		/// Handles changes to the aspect of the specified signal state.
		/// </summary>
		/// <param name="state">The signal state whose aspect has changed. Contains information about the signal's current and previous
		/// aspects.</param>
		private void OnAspectChanged(SignalState state)
		{
			LoggingReturn.DebugLog?.Invoke($"Signals Action triggered: Aspect changed: {state.Id} -> {state.CurrentAspectId ?? "OFF"}");
			_onAspectChanged?.Invoke(state.Id, state.CurrentAspectId ?? "OFF");
		}

		/// <summary>
		/// Handles changes to the mode of the specified signal.
		/// </summary>
		/// <param name="signalId"></param>
		/// <param name="newMode"></param>
		private void OnModeChanged(string signalId, SignalMode newMode)
		{
			LoggingReturn.DebugLog?.Invoke($"Signals Action triggered: Mode changed: {signalId} -> {newMode}");
			_onModeChanged?.Invoke(signalId, newMode.ToString());
		}

		/// <summary>
		/// Returns the current aspect of a signal by its ID, or null if not found.
		/// </summary>
		internal string? GetSignalAspect(string signalId)
		{
			try
			{
				return SignalsAPI.GetSignal(signalId)?.CurrentAspectId;
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"GetSignalAspect({signalId}) failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Returns all signals with raw world coordinates (not yet converted to lat/lng).
		/// </summary>
		internal Dictionary<string, object> GetAllSignals()
		{
			var result = new Dictionary<string, object>();
			try
			{
				var signals = SignalsAPI.GetAllSignals();
				if (signals == null)
				{
					LoggingReturn.DebugLog?.Invoke("GetAllSignals returned null.");
					return result;
				}

				LoggingReturn.DebugLog?.Invoke($"Found {signals.Count} signals.");
				foreach (var signal in signals)
				{
					var aspect = signal.CurrentAspectId ?? "OFF";
					//LoggingReturn.DebugLog?.Invoke($"Found signal: {signal.Id} at {signal.Position} with aspect {aspect} and mode {signal.Mode}");

					result[signal.Id] = new
					{
						signal.Id,
						signal.Type,
						signal.Mode,
						signal.CurrentAspectId,
						signal.IsOn,
						signal.Direction,
						signal.JunctionId,
						signal.SelectedBranch,
						signal.YardId,
						signal.TrackId,
						// Store raw world coordinates (x, z) - conversion to lat/lng happens in Session.cs
						Position = new[] { signal.Position.x, signal.Position.z },
					};
				}
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"GetAllSignals failed: {ex.Message}");
				return result;
			}

			return result;
		}

		/// <summary>
		/// Sets a signal to the specified aspect. Returns true on success.
		/// </summary>
		internal bool SetSignalAspect(string signalId, string aspect)
		{
			LoggingReturn.Log?.Invoke($"Attempting to set signal aspect: {signalId} -> {aspect}");
			try
			{
				return SignalsAPI.SetSignalAspect(signalId, aspect);
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"SetSignalAspect({signalId}, {aspect}) failed: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Sets a signal to the specified mode. Returns true on success.
		/// </summary>
		/// <param name="signalId">The ID of the signal</param>
		internal bool SetSignalMode(string signalId, string mode)
		{
			LoggingReturn.DebugLog?.Invoke($"Attempting to set signal mode: {signalId} -> {mode}");
			try
			{
				if (!Enum.TryParse<SignalMode>(mode, true, out var parsed))
				{
					LoggingReturn.DebugLog?.Invoke($"Failed to parse signal mode: {signalId} -> {mode}");
					return false;
				}
				return SignalsAPI.SetSignalMode(signalId, parsed);
			}
			catch (Exception ex)
			{
				LoggingReturn.Warning?.Invoke($"SetSignalMode({signalId}, {mode}) failed: {ex.Message}");
				return false;
			}
		}
	}
}
