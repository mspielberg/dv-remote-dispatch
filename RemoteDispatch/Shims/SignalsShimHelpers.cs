using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace DvMod.RemoteDispatch
{
	/// <summary>
	/// Helper methods for signal data projection and transformation.
	/// Contains functions that convert raw Signals API data to frontend-ready format.
	/// </summary>
	internal static class SignalsShimHelpers
	{
		/// <summary>
		/// Named container for minimal signal data sent to frontend.
		/// Contains only fields actually used by the JavaScript frontend.
		/// </summary>
		public class MinimalSignalData
		{
			public string? Id { get; set; }
			public string? CurrentAspectId { get; set; }
			public string? Mode { get; set; }
			public string? Type { get; set; }
			public JToken[]? Position { get; set; }
		}

		/// <summary>
		/// Extracts and converts Position field from signal JObject to lat/lng double array.
		/// Returns null if Position is missing or invalid (frontend will handle).
		/// </summary>
		public static JToken[]? GetLatLonArray(JObject signalObject)
		{
			const double metersToDegrees = 360.0 / 40e6;
			var positionArray = signalObject["Position"] as JArray;
			
			if (positionArray != null && positionArray.Count >= 2)
			{
				try
				{
					double x = positionArray[0].Value<double>();
					double z = positionArray[1].Value<double>();

					var worldOffset = WorldMover.currentMove;
					return new JToken[]
					{
						(z - worldOffset.z) * metersToDegrees,  // latitude (degrees)
						(x - worldOffset.x) * metersToDegrees   // longitude (degrees)
					};
				}
				catch
				{
					return null; // Position conversion failed
				}
			}
			
			return null; // Position format is invalid or missing
		}

		/// <summary>
		/// Retrieves the Aspect value and normalizes it to a string representation.
		/// </summary>
		public static object? GetNullableAsSignalAspect(JObject signalObject)
		{
			var aspectObj = signalObject["CurrentAspectId"];
			return aspectObj != null ? aspectObj.ToString() : "OFF";
		}

		/// <summary>
		/// Retrieves a field value and normalizes it to string, or returns default if missing/null.
		/// </summary>
		public static object? NormalizeToString(JObject signalObject, string fieldName, object? defaultValue)
		{
			var fieldValue = signalObject[fieldName];
			
			if (fieldValue != null && fieldValue.Type == JTokenType.String)
			{
				var strVal = fieldValue.ToString();
				if (!string.IsNullOrEmpty(strVal))
					return strVal;
			}
			
			// Handle null/missing field explicitly
			if (signalObject["$type"]?.ToString() != "NullType")
			{
				var rawValue = signalObject[fieldName];
				if (rawValue != null)
					return rawValue.ToString();
			}

			return defaultValue;
		}

		/// <summary>
		/// Strips the leading '#' character from signal IDs if present.
		/// SignalsAPI returns IDs with '#{ID}' format but expects methods to be called without the prefix.
		/// </summary>
		public static string StripSignalPrefix(string signalId)
		{
			return !string.IsNullOrEmpty(signalId) && signalId.StartsWith("#")
				? signalId.Substring(1)
				: signalId;
		}

		private static class HelpersInternal
		{
			// Internal helpers that shouldn't be exposed in public API
			internal const double MetersToDegreesConstant = 360.0 / 40e6;
		}

		/// <summary>
		/// Extension class for projecting signal data to minimal form.
		/// </summary>
		public static class MinimalSignalDataProjection
		{
			/// <summary>
			/// Projects raw signal data to minimal form containing only frontend-required fields.
			/// Strips 6 unused fields (IsOn, Direction, JunctionId, SelectedBranch, YardId, TrackId).
			/// Keeps only 5 used fields: Id, CurrentAspectId, Mode, Position, Type.
			/// </summary>
			public static Dictionary<string, MinimalSignalData> Create(Dictionary<string, object> rawSignals)
			{
				var minimalData = new Dictionary<string, MinimalSignalData>(StringComparer.Ordinal);

				foreach (var signal in rawSignals)
				{
					try
					{
						var signalObject = JObject.FromObject(signal.Value);
						minimalData[signal.Key] = CreateMinimalSignal(signalObject);
					}
					catch (Exception ex)
					{
						Main.Log($"Failed to project signal {signal.Key} to minimal form: {ex.Message}");
						// Include original data on error for debugging purposes  
						minimalData[signal.Key] = new MinimalSignalData
						{
							Id = "ERROR",
							CurrentAspectId = null,
							Mode = null, 
							Position = null,
							Type = null
						};
					}
				}

				return minimalData;
			}

			private static MinimalSignalData CreateMinimalSignal(JObject signalObject)
			{
				var currentAspect = GetNullableAsSignalAspect(signalObject)?.ToString() ?? "";
				var mode = NormalizeToString(signalObject, "Mode", null)?.ToString() ?? string.Empty;
				var position = GetLatLonArray(signalObject);
				var type = NormalizeToString(signalObject, "Type", null)?.ToString() ?? string.Empty;

				return new MinimalSignalData
				{
					Id = signalObject["Id"]?.ToString(),
					CurrentAspectId = currentAspect,
					Mode = mode,
					Position = position,
					Type = type
				};
			}
		}
	}
}
