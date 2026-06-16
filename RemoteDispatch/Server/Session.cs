using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DvMod.RemoteDispatch
{
    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private static readonly object allSesssionsLock = new object();
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> BaseTags = new HashSet<string>() { "cars", "jobs", "junctions", "player", "signals" };

        public static event Action<string>? OnSessionStarted;
        public static event Action<string>? OnSessionEnded;

        private class Session
        {
            public readonly string username;
            public readonly AsyncSet<string> pendingTags = new AsyncSet<string>();
            public readonly Stopwatch timeSinceLastFetch = new Stopwatch();

            public Session(string username)
            {
                this.username = username;
                foreach (var tag in BaseTags)
                {
                    AddTagIfPermitted(tag);
                }
            }

            public void AddTagIfPermitted(string tag)
            {
                if (username == null) return;
                switch (tag)
                {
                case "cars":
                    if (Main.settings.permissions.CanSeeLocomotives(username))
                    {
                        pendingTags.Add("carsWithLocomotives");
                    }
                    else
                    {
                        pendingTags.Add("cars");
                    }
                    break;
                case "player":
                    if (Main.settings.permissions.CanSeePlayerBlips(username))
                    {
                        pendingTags.Add("player");
                    }
                    else
                    {
                        pendingTags.Add("playerNull");
                    }
                    break;
                default:
                    pendingTags.Add(tag);
                    break;
                }
            }
        }

        public static HashSet<string> GetUsersWithActiveSessions()
        {
            return new HashSet<string>(allSessions.Values.Select(s => s.username));
        }


        public static void AddTag(string tag)
        {
            lock (allSesssionsLock)
            {
                List<string> timedOutSessions = new List<string>();
                foreach (var kvp in allSessions)
                {
                    var sessionId = kvp.Key;
                    var session = kvp.Value;
                    if (session.timeSinceLastFetch.Elapsed > SessionTimeout)
                        timedOutSessions.Add(sessionId);
                    else
                        session.AddTagIfPermitted(tag);
                }
                foreach (var sessionId in timedOutSessions)
                {
                    Main.Log($"Session {sessionId} timed out");
                    allSessions.Remove(sessionId);
                    OnSessionEnded?.Invoke(sessionId);
                }
            }
        }

        private static async Task<IEnumerable<string>> GetTags(string username, string sessionId)
        {
            Session session;
            lock (allSesssionsLock)
            {
                if (!allSessions.TryGetValue(sessionId, out session))
                {
                    Main.Log($"Starting new session {sessionId} for user {username}");
                    OnSessionStarted?.Invoke(username);
                    session = new Session(username);
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

        private static JObject? GetUpdateForCarGuid(string carGuid)
        {
            return CarData.GetCarGuidDataJson(carGuid);
        }

        private static JObject GetUpdateForTrainset(string trainsetId)
        {
            return JObject.FromObject(CarData.GetTrainsetData(int.Parse(trainsetId)));
        }

        private static JToken? GetUpdateForSplitTag(string tag)
        {
            var index = tag.IndexOf('-');
            var tagType = tag.Substring(0, index);
            var tagId = tag.Substring(index + 1);
            return tagType switch
            {
                "carguid" => GetUpdateForCarGuid(tagId),
                "trainset" => GetUpdateForTrainset(tagId),
                _ => throw new NotImplementedException($"Unexpected update tag {tag}"),
            };
        }

        private static JToken? GetUpdateForTag(string tag)
        {
            return tag switch
            {
                "cars" => JObject.FromObject(CarData.GetAllCarData(false).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson())),
                "carsWithLocomotives" => JObject.FromObject(CarData.GetAllCarData(true).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson())),
                "jobs" => JObject.FromObject(JobData.GetAllJobData()),
                "junctions" => new JArray(Junctions.GetAllJunctionStates()),
                "player" => PlayerData.GetPlayerData(),
                "playerNull" => new JObject(),
                "signals" => Main.settings.featureFlags.enableSignals ? SignalsShim.GetAllSignalsData() : new JObject(),
                _ when tag.Contains('-') => GetUpdateForSplitTag(tag),
                _ => throw new NotImplementedException($"Unexpected update tag {tag}"),
            };
        }

        private static string GetFrontendTagName(string tag)
        {
            return tag switch
            {
                "carsWithLocomotives" => "cars",
                "playerNull" => "player",
                _ => tag
            };
        }

        public static async Task<string> GetUpdates(string username, string sessionId)
        {
            var tags = await GetTags(username, sessionId).ConfigureAwait(false);
            return JsonConvert.SerializeObject(tags.ToDictionary(tag => GetFrontendTagName(tag), tag => GetUpdateForTag(tag)));
        }
    }
}
