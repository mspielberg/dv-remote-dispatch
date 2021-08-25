using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace DvMod.RemoteDispatch
{
    public static class EventSource
    {
        private static readonly byte[] MessagePrefix = Encoding.UTF8.GetBytes("data: ");
        private static readonly byte[] MessageTerminator = Encoding.UTF8.GetBytes("\n\n");

        private const int QueueLimit = 100;
        private static BlockingCollection<string>? messageQueue;

        private static readonly Dictionary<string, HttpListenerContext> activeContexts =
            new Dictionary<string, HttpListenerContext>();

        public static void PublishMessage(string message)
        {
            if (messageQueue == null)
            {
                messageQueue = new BlockingCollection<string>(QueueLimit);
                new Thread(() => PublisherMain(messageQueue)).Start();
                Main.DebugLog(() => "EventSource publisher thread started");
            }
            if (!messageQueue.TryAdd(message))
                Main.mod?.Logger.Warning("Messages are being dropped due to one or more slow clients");
        }

        private static void PublisherMain(BlockingCollection<string> messageQueue)
        {
            while (!messageQueue.IsCompleted)
            {
                if (messageQueue.TryTake(out var message))
                {
                    var deadContexts = new HashSet<string>();
                    var buf = Encoding.UTF8.GetBytes(message);

                    foreach (var (sessionId, context) in activeContexts)
                    {
                        var stream = context.Response.OutputStream;
                        try
                        {
                            stream.Write(MessagePrefix, 0, MessagePrefix.Length);
                            stream.Write(buf, 0, buf.Length);
                            stream.Write(MessageTerminator, 0, MessageTerminator.Length);
                            stream.Flush();
                            Main.DebugLog(() => $"Sent message to {sessionId}: {message}");
                        }
                        catch
                        {
                            deadContexts.Add(sessionId);
                        }
                    }

                    foreach (var sessionId in deadContexts)
                    {
                        Main.DebugLog(() => $"Lost connection to EventSource subscriber: {sessionId}");
                        activeContexts[sessionId].Response.Close();
                        activeContexts.Remove(sessionId);
                    }
                }
            }
            Main.DebugLog(() => "EventSource publisher thread shutting down");
        }

        public static void AddSession(string sessionId, HttpListenerContext context)
        {
            if (activeContexts.TryGetValue(sessionId, out var existingSession))
                existingSession.Response.Close();
            activeContexts[sessionId] = context;

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Add("Cache-Control", "no-store");
            context.Response.SendChunked = true;
            Main.DebugLog(() => $"New EventSource subscriber: {sessionId}");
        }

        public static void Shutdown()
        {
            messageQueue?.CompleteAdding();
            messageQueue = null;
            foreach (var (sessionId, context) in activeContexts)
            {
                Main.DebugLog(() => $"Closing connection to EventSource subscriber: {sessionId}");
                context.Response.Close();
            }
            activeContexts.Clear();
        }
    }
}