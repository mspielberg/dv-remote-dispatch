using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DvMod.RemoteDispatch
{
    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> AllTags = new HashSet<string>() { "cars", "jobs", "junctions", "player" };

        private class Session
        {
            public AsyncQueue<string> pendingTags = new AsyncQueue<string>();
            public Stopwatch timeSinceLastFetch = new Stopwatch();
        }

        public static void AddTag(string tag)
        {
            foreach (var (sessionId, session) in allSessions)
            {
                if (session.timeSinceLastFetch.Elapsed > SessionTimeout)
                {
                    Main.DebugLog(() => $"Session {sessionId} timed out");
                    allSessions.Remove(sessionId);
                }
                else if (!session.pendingTags.Contains(tag))
                {
                    // Might result in duplicates
                    session.pendingTags.Add(tag);
                }
            }
        }

        public static async Task<IEnumerable<string>> GetTags(string sessionId)
        {
            PlayerData.CheckTransform();
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

        public static async Task<string> GetTagsJson(string sessionId)
        {
            return JsonConvert.SerializeObject(await GetTags(sessionId).ConfigureAwait(false));
        }
    }
}