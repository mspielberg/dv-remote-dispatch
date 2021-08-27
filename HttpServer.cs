using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.IO.Compression;
using System.Text;
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
                Main.DebugLog(() => $"Starting HTTP server on port {Main.settings.serverPort}");
                listener.Start();
            }
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(true);
                    if (CheckAuthentication(context))
                    {
                        HandleRequest(context);
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
                catch (Exception e)
                {
                    Main.DebugLog(() => $"Exception while handling HTTP connection: {e}");
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
            return Main.settings.serverPassword.Length == 0
                || (context.User?.Identity is HttpListenerBasicIdentity identity &&
                    identity.Password == Main.settings.serverPassword);
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
            case "car":
                context.Response.ContentType = "application/json";
                Render200(context, CarData.GetAllCarDataJson());
                break;
            case "icon.svg":
                context.Response.ContentType = "image/svg+xml";
                RenderResource(context, "icon.svg");
                break;
            case "job":
                context.Response.ContentType = "application/json";
                Render200(context, JobData.GetAllJobDataJson());
                break;
            case "junction":
                HandleJunctionRequest(context);
                break;
            case "junctionState":
                context.Response.ContentType = "application/json";
                Render200(context, Junctions.GetJunctionStateJSON());
                break;
            case "leaflet.rotatedImageOverlay.js":
                context.Response.ContentType = "application/javascript";
                RenderResource(context, "leaflet.rotatedImageOverlay.js");
                break;
            case "main.js":
                context.Response.ContentType = "application/javascript";
                RenderResource(context, "main.js");
                break;
            case "player":
                context.Response.ContentType = "application/json";
                var json = PlayerData.GetPlayerDataJson();
                if (json != null)
                    Render200(context, json);
                else
                    RenderEmpty(context, 500);
                break;
            case "style.css":
                context.Response.ContentType = "text/css";
                RenderResource(context, "style.css");
                break;
            case "track":
                context.Response.ContentType = "application/json";
                Render200(context, RailTracks.GetTrackPointJSON());
                break;
            case "trainset":
                HandleTrainsetRequest(context);
                break;
            case "updates":
                HandleUpdatesRequest(context);
                break;
            default:
                RenderEmpty(context, 404);
                break;
            }
        }

        private static void HandleUpdatesRequest(HttpListenerContext context)
        {
            if (context.Request.Url.Segments.Length < 3)
            {
                RenderEmpty(context, 404);
                return;
            }

            var sessionId = context.Request.Url.Segments[2];
            context.Response.ContentType = "application/json";
            Render200(context, Sessions.GetTagsJson(sessionId));
        }

        private static void HandleJunctionRequest(HttpListenerContext context)
        {
            var url = context.Request.Url;
            switch (url.Segments.Length)
            {
            case 2:
                context.Response.ContentType = "application/json";
                Render200(context, Junctions.GetJunctionPointJSON());
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
                RenderEmpty(context, 404);
                break;
            default:
                RenderEmpty(context, 404);
                break;
            }
        }

        public static void HandleTrainsetRequest(HttpListenerContext context)
        {
            context.Response.ContentType = "application/json";
            var request = context.Request;
            if (request.Url.Segments.Length < 3)
            {
                RenderEmpty(context, 404);
                return;
            }
            var trainsetId = int.Parse(request.Url.Segments[2]);
            var carsJson = CarData.GetTrainsetDataJson(trainsetId);
            Render200(context, carsJson);
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

        private static void Render200(HttpListenerContext context, string s)
        {
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