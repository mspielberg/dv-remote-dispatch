using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using WebSocketSharp.Net;
using WebSocketSharp.Server;
using System.Collections.Generic;

namespace DvMod.RemoteDispatch
{
    public class HttpServer : MonoBehaviour
    {
        private static GameObject? rootObject;
        private WebSocketSharp.Server.HttpServer? server;

        private static readonly Dictionary<string, string> contentTypes = new Dictionary<string, string>{
            {"css", "text/css"},
            {"js", "application/javascript"},
            {"json", "application/json"},
            {"png", "image/png"},
            {"svg", "image/svg+xml"},
        };

        public void Start()
        {
            if (server != null)
                return;
            server = new WebSocketSharp.Server.HttpServer(Main.settings.serverPort)
            {
                AuthenticationSchemes = Main.settings.serverPassword.Length == 0 ? AuthenticationSchemes.Anonymous : AuthenticationSchemes.Basic,
                Realm = "DV Remote Dispatch",
                UserCredentialsFinder = identity => new NetworkCredential(identity.Name, Main.settings.serverPassword)
            };
            server.OnGet += OnGet;
            server.OnPost += OnPost;
            server.AddWebSocketService<WebSocketHandler>("/updates");
            Main.DebugLog(() => $"Starting HTTP server on port {Main.settings.serverPort}");
            server.Start();
        }

        public void OnDestroy()
        {
            if (server != null)
            {
                Main.DebugLog(() => "Stopping HTTP server");
                server.Stop();
            }
        }

        private static void OnPost(object _, HttpRequestEventArgs args)
        {
            var segments = args.Request.Url.Segments;

            if (segments.Length == 4
                && segments[1].TrimEnd('/') == "junction"
                && segments[3].TrimEnd('/') == "toggle"
                && int.TryParse(segments[2].TrimEnd('/'), out var junctionId)
                && junctionId >= 0
                && junctionId < JunctionsSaveManager.OrderedJunctions.Length)
            {
                Main.DebugLog(() => $"Toggling J-{junctionId}.");
                var junction = JunctionsSaveManager.OrderedJunctions[junctionId];
                Updater.PostAction(() => junction.Switch(Junction.SwitchMode.REGULAR)).Wait();
                Render200(args, contentTypes["json"], junction.selectedBranch.ToString());
            }
            else
            {
                RenderEmpty(args, 404);
            }
        }

        private static void OnGet(object _, HttpRequestEventArgs args)
        {
            var request = args.Request;
            if (request.Url.Segments.Length < 2)
            {
                args.Response.ContentType = "text/html; charset=UTF-8";
                RenderResource(args, "index.html");
                return;
            }

            string filename = request.Url.Segments[1].TrimEnd('/');
            if (filename.Contains("."))
            {
                // Handle bundled files generically.
                var assembly = typeof(HttpServer).Assembly;
                using var stream = assembly.GetManifestResourceStream(typeof(HttpServer), filename);
                if (stream != null)
                {
                    string ext = filename.Substring(filename.LastIndexOf(".") + 1);
                    // Only allow known content-types (ensures we can't accidentally send .dll files for example).
                    if (contentTypes.ContainsKey(ext))
                    {
                        Main.DebugLog(() => $"Found resource {filename}");
                        args.Response.ContentType = contentTypes[ext];
                        RenderResource(args, filename);
                        return;
                    }
                }

            }

            // Handle non-files.
            Main.DebugLog(() => $"Handling special type {filename}");
            switch (filename)
            {
                case "car":
                    Render200(args, contentTypes["json"], CarData.GetAllCarDataJson());
                    break;
                case "job":
                    Render200(args, contentTypes["json"], JobData.GetAllJobDataJson());
                    break;
                case "licenses":
                    Render200(args, contentTypes["json"], LicenseData.GetLicenseDataJson());
                    break;
                case "junction":
                    Render200(args, contentTypes["json"], Junctions.GetJunctionPointJSON());
                    break;
                case "junctionState":
                    Render200(args, contentTypes["json"], Junctions.GetJunctionStateJSON());
                    break;
                case "player":
                    var playerJson = PlayerData.GetPlayerDataJson();
                    if (playerJson != null)
                        Render200(args, contentTypes["json"], playerJson);
                    else
                        RenderEmpty(args, 500);
                    break;
                case "track":
                    Render200(args, contentTypes["json"], RailTracks.GetTrackPointJSON().Result);
                    break;
                case "trainset":
                    HandleTrainsetRequest(args);
                    break;
                default:
                    RenderEmpty(args, 404);
                    break;
            }
        }

        public static void HandleTrainsetRequest(HttpRequestEventArgs args)
        {
            var request = args.Request;
            if (request.Url.Segments.Length < 3)
            {
                RenderEmpty(args, 404);
                return;
            }
            var trainsetId = int.Parse(request.Url.Segments[2]);
            var carsJson = CarData.GetTrainsetDataJson(trainsetId);
            Render200(args, contentTypes["json"], carsJson);
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
            // ensure server shuts down immediately, not at the end of the frame
            GameObject.DestroyImmediate(rootObject);
            rootObject = null;
        }

        private static bool AcceptEncodingIncludes(HttpRequestEventArgs args, string encoding)
        {
            var headerValues = args.Request.Headers.Get("Accept-Encoding");
            if (headerValues == null)
                return false;
            return headerValues.Split(',').Select(x => x.Trim()).Any(x => x == encoding);
        }

        private static void RenderResource(HttpRequestEventArgs args, string resourceName)
        {
            var assembly = typeof(HttpServer).Assembly;
            using var stream = assembly.GetManifestResourceStream(typeof(HttpServer), resourceName);
            if (stream == null)
            {
                Main.DebugLog(() => $"Resource {resourceName} not found.");
                RenderEmpty(args, 404);
            }
            else if (AcceptEncodingIncludes(args, "gzip"))
            {
                args.Response.SendChunked = true;
                args.Response.Headers.Add("Content-Encoding", "gzip");
                using var gzip = new GZipStream(args.Response.OutputStream, CompressionMode.Compress);
                stream.CopyTo(gzip);
                gzip.Close();
                args.Response.Close();
            }
            else
            {
                args.Response.SendChunked = true;
                stream.CopyTo(args.Response.OutputStream);
                args.Response.Close();
            }
        }

        private static void Render200(HttpRequestEventArgs args, string contentType, string s)
        {
            args.Response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 128 && AcceptEncodingIncludes(args, "gzip"))
            {
                args.Response.SendChunked = true;
                args.Response.Headers.Add("Content-Encoding", "gzip");
                var mem = new MemoryStream(bytes);
                using var gzip = new GZipStream(args.Response.OutputStream, CompressionMode.Compress);
                mem.CopyTo(gzip);
            }
            else
            {
                args.Response.ContentLength64 = bytes.LongLength;
                args.Response.Close(bytes, false);
            }
        }

        private static void RenderEmpty(HttpRequestEventArgs args, int statusCode)
        {
            args.Response.StatusCode = statusCode;
            args.Response.Close();
        }
    }
}