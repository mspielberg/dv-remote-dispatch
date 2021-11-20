using System.Linq;
using UnityModManagerNet;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace DvMod.RemoteDispatch
{
    public class Settings : UnityModManager.ModSettings
    {
        public int serverPort = 7245;
        public string serverPassword = "";
        public bool startServerOnLoad = false;
        public Permissions permissions = new Permissions();
        public bool enableLogging = false;

        public readonly string? version = Main.mod?.Info.Version;

        const char EnDash = '\u2013';
        private string uncommittedPort = "initial";
        private string message = "";

        public void Draw()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(false));

            if (uncommittedPort == "initial")
                uncommittedPort = serverPort.ToString();

            GUILayout.Label($"Network port (1024{EnDash}65535)");
            uncommittedPort = GUILayout.TextField(uncommittedPort, maxLength: 5);
            uncommittedPort = new string(uncommittedPort.Where(c => char.IsDigit(c)).ToArray());
            bool isValidPort = int.TryParse(uncommittedPort, out var parsed) && parsed >= 1024 && parsed <= 65535;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start", GUILayout.ExpandWidth(false)))
            {
                if (isValidPort)
                {
                    serverPort = parsed;
                    HttpServer.Create();
                }
                else
                {
                    message = "Invalid port";
                }
            }

            if (GUILayout.Button("Stop", GUILayout.ExpandWidth(false)))
            {
                HttpServer.Destroy();
                message = "";
            }

            GUILayout.Label(message);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Password (blank for none)");
            serverPassword = GUILayout.TextField(serverPassword);
            GUILayout.EndHorizontal();

            startServerOnLoad = GUILayout.Toggle(startServerOnLoad, "Start server on load");

            permissions.Draw();

            enableLogging = GUILayout.Toggle(enableLogging, "Enable logging");

            GUILayout.EndVertical();
        }

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }
    }

    public class Permissions
    {
        public class PlayerPermissions
        {
            public string name;
            public bool canToggleJunctions;

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
            GUILayout.Label("Dispatcher permissions:");
            GUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
            DrawNamesColumn();
            DrawConnectedColumn();
            DrawJunctionsColumn();
            GUILayout.EndHorizontal();
        }

        private void DrawColumn(string label, Action<PlayerPermissions> action)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(label);
            foreach (var p in permissions)
                action(p);
            GUILayout.EndVertical();
        }

        private void DrawNamesColumn()
        {
            DrawColumn("Name", p => GUILayout.Label(p.name));
        }

        private void DrawConnectedColumn()
        {
            var connectedUsers = Sessions.GetUsersWithActiveSessions();
            DrawColumn("Connected", p => GUILayout.Toggle(connectedUsers.Contains(p.name), ""));
        }

        private void DrawJunctionsColumn()
        {
            DrawColumn("Junctions", p => p.canToggleJunctions = GUILayout.Toggle(p.canToggleJunctions, ""));
        }
    }
}
