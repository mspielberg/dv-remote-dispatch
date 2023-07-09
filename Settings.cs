using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Settings
    {
        public readonly ConfigEntry<ushort> serverPort;
        public readonly ConfigEntry<string> serverPassword;
        public readonly ConfigEntry<bool> startServerOnLoad;
        public readonly ConfigEntry<Permissions> permissions;
        public readonly ConfigEntry<bool> enableLogging;

        public Settings(ConfigFile configFile)
        {
            if (!TomlTypeConverter.CanConvert(typeof(Permissions)))
                TomlTypeConverter.AddConverter(typeof(Permissions), new TypeConverter {
                    ConvertToObject = Permissions.ConvertToObject,
                    ConvertToString = Permissions.ConvertToString
                });
            serverPort = configFile.Bind("Server", "Port", (ushort)7245, new ConfigDescription("Network port to listen on", new AcceptableValueRange<ushort>(1024, 49151)));
            serverPassword = configFile.Bind("Server", "Password", "", "The password required to connect to the server");
            startServerOnLoad = configFile.Bind("Server", "Start on Load", false, "Whether the server should start automatically when the game loads");
            permissions = configFile.Bind("Server", "Permissions", new Permissions(), new ConfigDescription("Permissions for each user", null, new ConfigurationManagerAttributes { CustomDrawer = _ => permissions?.Value?.Draw() }));
            configFile.Bind("Server", "Start Server", false, new ConfigDescription("", null, new ConfigurationManagerAttributes { CustomDrawer = DrawStartStop, HideDefaultButton = true, ReadOnly = true }));
            enableLogging = configFile.Bind("Debug", "Logging", false, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

        private static void DrawStartStop(ConfigEntryBase _)
        {
            if (!WorldStreamingInit.isLoaded)
            {
                GUILayout.Label("Waiting for session");
                return;
            }

            if (!HttpServer.IsRunning())
            {
                if (GUILayout.Button("Start", GUILayout.ExpandWidth(false)))
                    HttpServer.Create();
            }
            else
            {
                if (GUILayout.Button("Stop", GUILayout.ExpandWidth(false)))
                    HttpServer.Destroy();
            }
        }
    }

    [Serializable]
    public class Permissions
    {
        [Serializable]
        public class PlayerPermissions
        {
            public string name;
            public bool canToggleJunctions;
            public bool canControlLocomotives;

            public PlayerPermissions()
            {
                name = "";
            }

            public PlayerPermissions(string name)
            {
                this.name = name;
            }
        }

        public readonly List<PlayerPermissions> permissions = new List<PlayerPermissions>();

        public Permissions()
        {
            Sessions.OnSessionStarted += OnSessionStarted;
        }

        public bool HasJunctionPermission(string username)
        {
            return permissions.Find(p => p.name == username)?.canToggleJunctions ?? false;
        }

        public bool HasLocoControlPermission(string username)
        {
            return permissions.Find(p => p.name == username)?.canControlLocomotives ?? false;
        }

        private void OnSessionStarted(string username)
        {
            if (!permissions.Any(p => p.name == username))
            {
                permissions.Add(new PlayerPermissions(username));
                permissions.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
            }
        }

        public void Draw()
        {
            GUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
            DrawNamesColumn();
            DrawConnectedColumn();
            DrawJunctionsColumn();
            DrawLocoControlColumn();
            GUILayout.EndHorizontal();
        }

        private void DrawColumn(string label, Action<PlayerPermissions> action)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(label);
            foreach (PlayerPermissions? p in permissions)
                action(p);
            GUILayout.EndVertical();
        }

        private void DrawNamesColumn()
        {
            DrawColumn("Name", p => GUILayout.Label(p.name));
        }

        private void DrawConnectedColumn()
        {
            HashSet<string> connectedUsers = Sessions.GetUsersWithActiveSessions();
            DrawColumn("Connected", p => GUILayout.Toggle(connectedUsers.Contains(p.name), ""));
        }

        private void DrawJunctionsColumn()
        {
            DrawColumn("Junctions", p => p.canToggleJunctions = GUILayout.Toggle(p.canToggleJunctions, ""));
        }

        private void DrawLocoControlColumn()
        {
            DrawColumn("Locomotive Control", p => p.canControlLocomotives = GUILayout.Toggle(p.canControlLocomotives, ""));
        }

        public static string ConvertToString(object obj, Type _)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static object ConvertToObject(string str, Type _)
        {
            return JsonConvert.DeserializeObject<Permissions>(str) ?? new Permissions();
        }
    }
}
