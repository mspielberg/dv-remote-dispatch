using DV.Utils;
using DV;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using UnityEngine;
using DvMod.RemoteDispatch.Patches.Game;

namespace DvMod.RemoteDispatch
{
    public class HttpServer : MonoBehaviour
    {
        private static GameObject? rootObject;
        private readonly HttpListener listener = new HttpListener();

        public async void Start()
        {
            string[] prefixes = new string[]
            {
        "http://localhost",
        "http://127.0.0.1",
        "http://[::1]",
        "http://*",
        "http://[::]"
            };

            if (!listener.IsListening)
            {
                foreach (string prefix in prefixes)
                {
                    listener.Prefixes.Add(prefix + ':' + Main.settings.serverPort + '/');
                    Main.DebugLog(() => $"Added prefix: {prefix + ':' + Main.settings.serverPort + '/'}");
                }

                listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous | AuthenticationSchemes.Basic;
                listener.Realm = "DV Remote Dispatch";

                Main.DebugLog(() => $"Starting HTTP server on port {Main.settings.serverPort}");
                try
                {
                    listener.Start();
                    Main.DebugLog(() => "HTTP server started successfully");
                }
                catch (Exception e)
                {
                    Main.DebugLog(() => $"Failed to start HTTP server: {e.Message}");
                    return;
                }
            }

            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(true);
                    Main.DebugLog(() => $"Received connection from: {context.Request.RemoteEndPoint}, URL: {context.Request.Url}, Local endpoint: {context.Request.LocalEndPoint}");

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
                                Main.DebugLog(() => $"Exception while handling HTTP request ({context.Request.Url}): {e}");
                            }
                        });
                    }
                    else
                    {
                        context.Response.Headers.Add("WWW-Authenticate", "Basic");
                        RenderEmpty(context, 401);
                        Main.DebugLog(() => $"Authentication failed for request from: {context.Request.RemoteEndPoint}");
                    }
                }
                catch (ObjectDisposedException e) when (e.ObjectName == "listener")
                {
                    Main.DebugLog(() => "HTTP server stopped");
                }
                catch (Exception e)
                {
                    Main.DebugLog(() => $"Unexpected error in server loop: {e}");
                }
            }
        }

        public void OnDestroy()
        {
            if (listener.IsListening)
            {
                Main.DebugLog(() => "Stopping HTTP server");
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
            context.Response.AddHeader("morm-debug", context.Request.RemoteEndPoint.Address.ToString());
            if (request.Url.Segments.Length < 2)
            {
                context.Response.ContentType = ContentTypes.Html;
                RenderResource(context, "index.html");
                return;
            }

            switch (request.Url.Segments[1].TrimEnd('/'))
            {
            case "car":
                HandleCarRequest(context);
                break;
            case "job":
                Render200(context, ContentTypes.Json, JobData.GetAllJobDataJson());
                break;
            case "junction":
                HandleJunctionRequest(context);
                break;
            case "junctionState":
                Render200(context, ContentTypes.Json, Junctions.GetJunctionStateJSON());
                break;
            case "player":
                var playerJson = PlayerData.GetPlayerDataJson();
                if (playerJson != null)
                    Render200(context, ContentTypes.Json, playerJson);
                else
                    RenderEmpty(context, 500);
                break;
            case "res":
                RenderResource(context);
                break;
            case "track":
                Render200(context, ContentTypes.Json, await RailTracks.GetTrackPointJSON().ConfigureAwait(false));
                break;
            case "trainset":
                HandleTrainsetRequest(context);
                break;
            case "updates":
                await HandleUpdatesRequest(context).ConfigureAwait(false);
                break;
            default:
                RenderEmpty(context, 404);
                break;
            }
        }

        private static async void HandleCarRequest(HttpListenerContext context)
        {
            var segments = context.Request.Url.Segments;
            if (segments.Length == 2 && context.Request.HttpMethod == "GET")
            {
                var allCarDataJson = CarData.GetAllCarDataJson();
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
            return junctionId >= 0 && junctionId < SingletonBehaviour<WorldData>.Instance.OrderedJunctions.Length;
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
                        Main.DebugLog(() => $"Toggling J-{junctionId}.");
                        var junction = SingletonBehaviour<WorldData>.Instance.OrderedJunctions[junctionId];
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
            RenderResource(context, resourceName);
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