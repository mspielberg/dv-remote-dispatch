using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class HttpServer : MonoBehaviour
    {
        private static GameObject? rootObject;
        private HttpListener listener = new HttpListener();

        public async void Start()
        {
            if (!listener.IsListening)
            {
                listener.Prefixes.Add($"http://*:{Main.settings.serverPort}/");
                Main.DebugLog(() => $"Starting HTTP server on port {Main.settings.serverPort}");
                listener.Start();
            }
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    HandleRequest(context);
                }
                catch (ObjectDisposedException)
                {
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

        private static void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            if (request.Url.Segments.Length < 2)
            {
                context.Response.ContentType = "text/html; charset=UTF-8";
                RenderResource(context, "index.html");
                return;
            }

            switch (request.Url.Segments[1].TrimEnd('/'))
            {
            case "main.js":
                context.Response.ContentType = "application/javascript";
                RenderResource(context, "main.js");
                break;
            case "junction":
                HandleJunctionRequest(context);
                break;
            case "junctionState":
                context.Response.ContentType = "application/json";
                Render200(context, RailTracks.GetJunctionStateJSON());
                break;
            case "track":
                context.Response.ContentType = "application/json";
                Render200(context, RailTracks.GetTrackPointJSON());
                break;
            case "trackCoordinates":
                int.TryParse(request.Url.Query.Substring(1), out var resolution);
                Render200(context, RenderTrackCoordinates(RailTracks.GetAllTrackPoints(resolution)));
                break;
            case "normalizedCoordinates":
                Render200(context, RenderTrackCoordinates(RailTracks.GetNormalizedTrackCoordinates()));
                break;
            default:
                Render404(context);
                break;
            }
        }

        private static void HandleJunctionRequest(HttpListenerContext context)
        {
            var url = context.Request.Url;
            switch (url.Segments.Length)
            {
            case 2:
                context.Response.ContentType = "application/json";
                Render200(context, RailTracks.GetJunctionPointJSON());
                break;
            case 4:
                var junctionIdString = url.Segments[2].TrimEnd('/');
                if (int.TryParse(junctionIdString, out var junctionId) && url.Segments[3] == "toggle")
                {
                    if (junctionId >= 0 && junctionId < JunctionsSaveManager.OrderedJunctions.Length)
                    {
                        Main.DebugLog(() => $"Toggling J-{junctionId}.");
                        JunctionsSaveManager.OrderedJunctions[junctionId].Switch(Junction.SwitchMode.REGULAR);
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                        return;
                    }
                }
                Render404(context);
                break;
            default:
                Render404(context);
                break;
            }

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
            GameObject.Destroy(rootObject);
            rootObject = null;
        }

        private static void RenderResource(HttpListenerContext context, string resourceName)
        {
            var assembly = typeof(HttpServer).Assembly;
            using var stream = assembly.GetManifestResourceStream(typeof(HttpServer), resourceName);
            stream.CopyTo(context.Response.OutputStream);
            context.Response.Close();
        }

        private static void Render200(HttpListenerContext context, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 1024 && (context.Request.Headers.GetValues("Accept-Encoding")?.Contains("gzip") ?? false))
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

        private static void Render404(HttpListenerContext context)
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        private static string RenderTrackCoordinates(IEnumerable<IEnumerable<(float x, float z)>> coords)
        {
            return string.Join("\n", coords.Select(trackPoints => string.Join(",", trackPoints)));
        }
    }
}