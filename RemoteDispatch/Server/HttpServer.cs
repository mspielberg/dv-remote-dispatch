using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
	public class HttpServer : MonoBehaviour
	{
		private static GameObject? rootObject;
		private readonly HttpListener listener = new HttpListener();

		public async void Start()
		{
			if (!listener.IsListening)
			{
				listener.Prefixes.Add($"http://*:{Main.settings.serverPort}/");
				listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous | AuthenticationSchemes.Basic;
				listener.Realm = "DV Remote Dispatch";
				Main.Log($"Starting HTTP server on port {Main.settings.serverPort}");
				listener.Start();
			}

			while (listener.IsListening)
			{
				try
				{
					var context = await listener.GetContextAsync().ConfigureAwait(true);
					if (CheckAuthentication(context))
					{
						_ = Task.Run(async () =>
						{
							try
							{
								await HandleRequest(context).ConfigureAwait(false);
							}
							catch (Exception e)
							{
								Main.Log($"Exception while handling HTTP request ({context.Request.Url}): {e}");
							}
						});
					}
					else
					{
						context.Response.Headers.Add("WWW-Authenticate", "Basic");
						RenderEmpty(context, 401);
					}
				}
				catch (ObjectDisposedException e) when (e.ObjectName == "listener")
				{
					// ignore when OnDestroy() is called to shutdown the server
				}
			}
		}

		public void OnDestroy()
		{
			if (listener.IsListening)
			{
				Main.Log("Stopping HTTP server");
				listener.Stop();
				listener.Prefixes.Clear();
			}
		}

		private static bool CheckAuthentication(HttpListenerContext context)
		{
			string serverPassword = Main.settings.serverPassword;
			return context.User?.Identity is HttpListenerBasicIdentity identity && (string.IsNullOrEmpty(serverPassword) || identity.Password == serverPassword);
		}

		private static async Task HandleRequest(HttpListenerContext context)
		{
			var request = context.Request;
			if (request.Url.Segments.Length < 2)
			{
				context.Response.ContentType = ContentTypes.Html;
				RenderResource(context, "frontend.index.html");
				return;
			}

			switch (request.Url.Segments[1].TrimEnd('/'))
			{
			case "car":
#if DEBUG
				Main.Log("/car endpoint hit");
#endif
				HandleCarRequest(context);
				break;
			case "junction":
				HandleJunctionRequest(context);
#if DEBUG
				Main.Log("/junction endpoint hit");
#endif
				break;
			case "player":
#if DEBUG
				Main.Log("/player endpoint hit");
#endif
				if(!Main.settings.permissions.CanSeePlayerBlips(context.User.Identity.Name))
				{
					RenderEmpty(context, 200);
					break;
				}
				var playerJson = PlayerData.GetPlayerDataJson();
				if (playerJson != null)
					Render200(context, ContentTypes.Json, playerJson);
				else
					RenderEmpty(context, 500);
				break;
			case "res":
#if DEBUG
				Main.Log("/res endpoint hit");
#endif
				RenderResource(context);
				break;
			case "track":
#if DEBUG
				Main.Log("/track endpoint hit");
#endif
				Render200(context, ContentTypes.Json, await RailTracks.GetTrackPointJSON().ConfigureAwait(false));
				break;
			case "updates":
#if DEBUG
				Main.Log("/updates endpoint hit");
#endif
				await HandleUpdatesRequest(context).ConfigureAwait(false);
				break;
			case "signals":
#if DEBUG
				Main.Log("/signals endpoint hit");
#endif
				string signalsJson = Main.settings.featureFlags.enableSignals ? JsonConvert.SerializeObject(SignalsShim.GetAllSignalsData()) : JsonConvert.SerializeObject(new JObject());
				Render200(context, ContentTypes.Json, signalsJson);
				break;
			case "signal":
#if DEBUG
				Main.Log("/signal endpoint hit");
#endif
				await HandleSignalRequest(context);
				break;
			default:
#if DEBUG
				Main.Log("unknown endpoint hit");
#endif
				RenderEmpty(context, 404);
				break;
			}
		}

		private static async void HandleCarRequest(HttpListenerContext context)
		{
			var segments = context.Request.Url.Segments;
			if (segments.Length == 2 && context.Request.HttpMethod == "GET")
			{
				var allCarDataJson = CarData.GetAllCarDataJson(Main.settings.permissions.CanSeeLocomotives(context.User.Identity.Name));
				Render200(context, allCarDataJson);
				return;
			}

			if (segments.Length == 3 && context.Request.HttpMethod == "GET")
			{
				var carGuid = segments[2].TrimEnd('/');
				var carDataJson = CarData.GetCarGuidDataJson(carGuid);
				if (carDataJson == null)
					RenderEmpty(context, 404);
				else
					Render200(context, carDataJson);
				return;
			}

			if (segments.Length == 4 && segments[3] == "control" && context.Request.HttpMethod == "POST")
			{
				var carGuid = segments[2].TrimEnd('/');
				var controller = LocoControl.GetLocoController(carGuid);
				if (controller == null)
				{
					RenderEmpty(context, 404);
					return;
				}
				if (!Main.settings.permissions.HasLocoControlPermission(context.User.Identity.Name))
				{
					RenderEmpty(context, 403);
					return;
				}
				var success = await Updater.RunOnMainThread(() =>
					LocoControl.RunCommand(controller, context.Request.QueryString)
				).ConfigureAwait(false);
				RenderEmpty(context, success ? 204 : 400);
			}
			RenderEmpty(context, 404);
		}

		private static async Task HandleSignalRequest(HttpListenerContext context)
		{
			var url = context.Request.Url;
			var segments = url.Segments;

			if (segments.Length < 3 || !segments[2].TrimEnd('/').Equals("control", StringComparison.OrdinalIgnoreCase))
			{
				Main.Warning($"Invalid signal control request URL: {url}");
				Main.DebugLog($"Number of URL segments: {segments.Length}");
				Main.DebugLog($"First URL segment (should be host): {(segments.Length >= 1 ? segments[0] : "N/A")}");
				Main.DebugLog($"Second URL segment (should be 'signal'): {(segments.Length >= 2 ? segments[1] : "N/A")}");
				Main.DebugLog($"Third URL segment (should be signal ID): {(segments.Length >= 3 ? segments[2] : "N/A")}");
				Main.DebugLog($"Fourth URL segment (should be 'control'): {(segments.Length >= 4 ? segments[3] : "N/A")}");
				RenderEmpty(context, 404);
				return;
			}

			if (!Main.settings.permissions.HasSignalControlPermission(context.User.Identity.Name))
			{
				RenderEmpty(context, 403);
				return;
			}

			bool success = true;
			bool bodyRead = false;
			string? mode = null;
			string? aspect = null;
			string? signalId = null;

			try
			{
				const int maxBodySize = 65536;
				using var stream = context.Request.InputStream;
				var buffer = new byte[8192];
				int totalRead = 0;

				while (stream.CanRead)
				{
					int 					bytesRead = stream.Read(buffer, 0, buffer.Length);
					if (bytesRead == 0) break;
					
					totalRead += bytesRead;
					if (totalRead > maxBodySize)
					{
						throw new Exception("Request body too large");
					}
				}

				string? bodyText = Encoding.UTF8.GetString(buffer, 0, totalRead);

				if (!string.IsNullOrEmpty(bodyText))
				{
					var requestData = JObject.Parse(bodyText);
					bodyRead = true;

					if (requestData.TryGetValue("signalId", out JToken? idToken))
					{
						signalId = idToken.Value<string?>();
					}

					if (requestData.TryGetValue("mode", out JToken? modeToken))
					{
						mode = modeToken.Value<string?>();
					}
					else if (requestData.TryGetValue("aspect", out JToken? aspectToken))
					{
						aspect = aspectToken.Value<string?>();
					}
				}

				if (string.IsNullOrEmpty(signalId))
				{
					Main.Warning("Signal control request missing 'signalId' in body");
					RenderEmpty(context, 400);
					return;
				}

				if (mode != null)
				{
					Main.DebugLog($"Setting signal {signalId} mode to {mode}");
					bool result = SignalsShim.SetSignalMode(signalId!, mode!);

					if (!result && SignalsShim.IsInitialized == false)
					{
						Main.Warning($"[SIGNAL] Integration not initialized - cannot set mode: {mode}/{signalId}");
						Main.DebugLog("  -> Check that DVSignals mod is enabled in GameMods list");
					}
					else if (!result && SignalsShim.IsInitialized == true)
					{
						Main.Warning($"[SIGNAL] API call failed: signal={signalId}, mode={mode}");
						Main.DebugLog("  -> Check Signal exists, spelling correct, aspect not already set");
					}

					success = result;
				}
				else if (aspect != null)
				{
					Main.DebugLog($"Setting signal {signalId} aspect to {aspect}");
					bool result2 = SignalsShim.SetSignalAspect(signalId!, aspect!);

					if (!result2)
					{
						Main.Warning($"[SIGNAL] API call failed: signal={signalId}, aspect={aspect}");
					}

					success &= result2;
				}

				if (!bodyRead)
				{
					Main.Warning($"Signal control request lacks 'mode' or 'aspect'): {url}");
				}
			}
			catch (Exception e)
			{
				Main.Warning($"Failed to parse signal control request body: {e.Message}");
				success = false;
			}

			RenderEmpty(context, success ? 204 : 400);
		}

		private static async Task HandleUpdatesRequest(HttpListenerContext context)
		{
			if (context.Request.Url.Segments.Length < 3)
			{
				RenderEmpty(context, 404);
				return;
			}

			var username = context.User?.Identity?.Name ?? "";
			var sessionId = context.Request.Url.Segments[2];
			Render200(context, ContentTypes.Json, await Sessions.GetUpdates(username, sessionId).ConfigureAwait(false));
		}

		private static bool IsValidJunctionId(int junctionId)
		{
			return junctionId >= 0 && junctionId < RailTrackRegistry.Instance.OrderedJunctions.Length;
		}

		private static async void HandleJunctionRequest(HttpListenerContext context)
		{
			var url = context.Request.Url;
			switch (url.Segments.Length)
			{
			case 2:
				Render200(context, ContentTypes.Json, Junctions.GetJunctionPointJSON());
				break;
			case 4:
				var junctionIdString = url.Segments[2].TrimEnd('/');
				if (int.TryParse(junctionIdString, out var junctionId) && url.Segments[3] == "toggle" && IsValidJunctionId(junctionId))
				{
					if (!Main.settings.permissions.HasJunctionPermission(context.User.Identity.Name))
					{
						RenderEmpty(context, 403);
						return;
					}
					var newSelectedBranch = await Updater.RunOnMainThread(() =>
					{
						Main.DebugLog($"Toggling J-{junctionId}.");
						var junction = RailTrackRegistry.Instance.OrderedJunctions[junctionId];
						junction.Switch(Junction.SwitchMode.REGULAR);
						return junction.selectedBranch;
					}).ConfigureAwait(false);
					Render200(context, new JValue(newSelectedBranch));
					return;
				}
				RenderEmpty(context, 404);
				break;
			default:
				RenderEmpty(context, 404);
				break;
			}
		}

		public static void HandleTrainsetRequest(HttpListenerContext context)
		{
			var request = context.Request;
			if (request.Url.Segments.Length < 3)
			{
				RenderEmpty(context, 404);
				return;
			}
			var trainsetId = int.Parse(request.Url.Segments[2]);
			Render200(context, CarData.GetTrainsetDataJson(trainsetId));
		}

		public static void Create()
		{
			if (rootObject == null)
			{
				rootObject = new GameObject();
				GameObject.DontDestroyOnLoad(rootObject);
				rootObject.AddComponent<HttpServer>();
			}
		}

		public static void Destroy()
		{
			if (rootObject == null)
				return;
			// ensure server shuts down immediately, not at the end of the frame
			DestroyImmediate(rootObject);
			rootObject = null;
		}

		private static void RenderResource(HttpListenerContext context)
		{
			var resourceName = context.Request.Url.Segments[2];
			var extension = Path.GetExtension(resourceName);
			context.Response.ContentType = ContentTypes.ForExtension(extension);
			RenderResource(context, $"frontend.{resourceName}");
		}

		private static void RenderResource(HttpListenerContext context, string resourceName)
		{
			var assembly = typeof(HttpServer).Assembly;
			using var stream = assembly.GetManifestResourceStream(typeof(HttpServer), resourceName);
			if (stream == null)
			{
				RenderEmpty(context, 404);
			}
			else
			{
				stream.CopyTo(context.Response.OutputStream);
				context.Response.Close();
			}
		}

		private static class ContentTypes
		{
			public const string Css = "text/css";
			public const string Html = "text/html; charset=UTF-8";
			public const string Json = "application/json";
			public const string Javascript = "application/javascript";
			public const string Png = "image/png";
			public const string Svg = "image/svg+xml";

			public static string ForExtension(string extension)
			{
				return extension switch
				{
					".css" => Css,
					".js" => Javascript,
					".json" => Json,
					".png" => Png,
					".svg" => Svg,
					_ => "",
				};
			}
		}

		private static void Render200(HttpListenerContext context, JToken json)
		{
			Render200(context, ContentTypes.Json, JsonConvert.SerializeObject(json));
		}

		private static void Render200(HttpListenerContext context, string contentType, string s)
		{
#if DEBUG
			Main.Log("Render200");
#endif
			context.Response.ContentType = contentType;
			var bytes = Encoding.UTF8.GetBytes(s);
			if (bytes.Length > 128 && (context.Request.Headers.GetValues("Accept-Encoding")?.Contains("gzip") ?? false))
			{
				context.Response.Headers.Add("Content-Encoding", "gzip");
				var mem = new MemoryStream(bytes);
				using var gzip = new GZipStream(context.Response.OutputStream, CompressionMode.Compress);
				mem.CopyTo(gzip);
			}
			else
			{
				context.Response.Close(bytes, false);
			}
		}

		private static void RenderEmpty(HttpListenerContext context, int statusCode)
		{
			context.Response.StatusCode = statusCode;
			context.Response.Close();
		}
	}
}
