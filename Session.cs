using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace DvMod.RemoteDispatch
{
    public class WebSocketHandler : WebSocketBehavior
    {
        private string SessionId { get => $"{Context.User?.Identity?.Name}@{Context.UserEndPoint}"; }

        protected override void OnMessage(MessageEventArgs e)
        {
            Send(RemoteDispatch.Sessions.GetUpdates(SessionId).Result);
        }

        protected override void OnOpen()
        {
            Main.DebugLog(() => $"Started new WebSocket connection for {SessionId}.");
        }
    }

    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> AllTags = new HashSet<string>() { "cars", "jobs", "junctions", "player" };

        private class Session
        {
            public AsyncSet<string> pendingTags = new AsyncSet<string>();
            public Stopwatch timeSinceLastFetch = new Stopwatch();
        }

        public static void AddTag(string tag)
        {
            foreach (var kvp in allSessions)
            {
                var sessionId = kvp.Key;
                var session = kvp.Value;
                if (session.timeSinceLastFetch.Elapsed > SessionTimeout)
                {
                    Main.DebugLog(() => $"Session {sessionId} timed out");
                    allSessions.Remove(sessionId);
                }
                else
                {
                    session.pendingTags.Add(tag);
                }
            }
        }

        private static async Task<IEnumerable<string>> GetTags(string sessionId)
        {
            if (!allSessions.TryGetValue(sessionId, out var session))
            {
                Main.DebugLog(() => $"Starting new session {sessionId}");
                session = new Session();
                foreach (var tag in AllTags)
                    session.pendingTags.Add(tag);
                allSessions.Add(sessionId, session);
            }

            session.timeSinceLastFetch.Restart();

            var tags = session.pendingTags.TakeAll().ToHashSet();
            if (tags.Count > 0)
                return tags;

            // No data available
            var (success, awaitedTag) = await session.pendingTags.TryTakeAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            return success ? new string[1] { awaitedTag } : new string[0];
        }

        private static JObject GetUpdateForTrainset(string tag)
        {
            var segments = tag.Split('-');
            var trainsetId = segments[1];
            return JObject.FromObject(CarData.GetTrainsetData(int.Parse(trainsetId)));
        }

        private static JToken GetUpdateForTag(string tag)
        {
            return tag switch
            {
                "cars" => JObject.FromObject(CarData.GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson())),
                "jobs" => JObject.FromObject(JobData.GetAllJobData()),
                "junctions" => new JArray(Junctions.GetAllJunctionStates()),
                "player" => PlayerData.GetPlayerData(),
                "licenses" => JObject.FromObject(LicenseData.GetLicenseData()),
                var other => GetUpdateForTrainset(other),
            };
        }

        public static async Task<string> GetUpdates(string sessionId)
        {
            var tags = await GetTags(sessionId).ConfigureAwait(false);
            return JsonConvert.SerializeObject(tags.ToDictionary(tag => tag, tag => GetUpdateForTag(tag)));
        }
    }
}