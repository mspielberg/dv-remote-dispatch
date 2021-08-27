using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;

namespace DvMod.RemoteDispatch
{
    public static class Sessions
    {
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(1);
        private static readonly Dictionary<string, Session> allSessions = new Dictionary<string, Session>();
        private static readonly HashSet<string> AllTags = new HashSet<string>() { "cars", "jobs", "junctions", "player" };

        private class Session
        {
            public HashSet<string> pendingTags = new HashSet<string>();
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
                else
                {
                    session.pendingTags.Add(tag);
                }
            }
        }

        public static IEnumerable<string> GetTags(string sessionId)
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
            var tags = session.pendingTags;
            session.pendingTags = new HashSet<string>();
            session.timeSinceLastFetch.Restart();
            Main.DebugLog(() => $"Clearing tags for session{sessionId}");
            return tags;
        }

        public static string GetTagsJson(string sessionId)
        {
            return JsonConvert.SerializeObject(GetTags(sessionId));
        }
    }
}