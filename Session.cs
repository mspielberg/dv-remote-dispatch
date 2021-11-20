using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DvMod.RemoteDispatch
{
    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private static readonly object allSesssionsLock = new object();
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> AllTags = new HashSet<string>() { "cars", "jobs", "junctions", "player" };

        private class Session
        {
            public AsyncSet<string> pendingTags = new AsyncSet<string>();
            public Stopwatch timeSinceLastFetch = new Stopwatch();
        }

        public static void AddTag(string tag)
        {
            lock (allSesssionsLock)
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
        }

        private static async Task<IEnumerable<string>> GetTags(string sessionId)
        {
            Session session;
            lock (allSesssionsLock)
            {
                if (!allSessions.TryGetValue(sessionId, out session))
                {
                    Main.DebugLog(() => $"Starting new session {sessionId}");
                    session = new Session();
                    foreach (var tag in AllTags)
                        session.pendingTags.Add(tag);
                    allSessions.Add(sessionId, session);
                }
            }

            session.timeSinceLastFetch.Restart();

            var tags = new HashSet<string>(session.pendingTags.TakeAll());
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