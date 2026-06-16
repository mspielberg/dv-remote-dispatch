using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    public class Settings : UnityModManager.ModSettings
    {
        public int serverPort = 7245;
        public string serverPassword = "";
        public Permissions permissions = new Permissions();
        public FeatureFlags featureFlags = new FeatureFlags();
        public bool showUndiscoveredLocomotives = false;
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
            GUILayout.Label(message);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Password (blank for none)");
            serverPassword = GUILayout.TextField(serverPassword);
            GUILayout.EndHorizontal();

            permissions.Draw();

            var newShowUndiscoveredLocomotives = GUILayout.Toggle(
                showUndiscoveredLocomotives,
                "Show undiscovered locomotives");
            if (newShowUndiscoveredLocomotives != showUndiscoveredLocomotives)
            {
                showUndiscoveredLocomotives = newShowUndiscoveredLocomotives;
                CarUpdater.ForceCarRefresh();
            }

            enableLogging = GUILayout.Toggle(enableLogging, "Enable logging");

            featureFlags.Draw();

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
			public bool canControlLocomotives;
			public bool canSeePlayerBlips;
			public bool canSeeLocomotives;
			public bool canControlSignals;

			public PlayerPermissions()
            {
                name = "";
            }

            public PlayerPermissions(string name)
            {
                this.name = name;
            }

            public PlayerPermissions ShallowCopy()
            {
                return (PlayerPermissions)MemberwiseClone();
            }
        }

        public readonly List<PlayerPermissions> permissions = new List<PlayerPermissions>();
	public PlayerPermissions defaultPermissions = new PlayerPermissions{
		name = "Default",
		canToggleJunctions =  true,
		canControlLocomotives = true,
		canSeePlayerBlips = true,
		canSeeLocomotives = true,
		canControlSignals = false
	};

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

		public bool HasSignalControlPermission(string username)
		{
			return permissions.Find(p => p.name == username)?.canControlSignals ?? false;
		}
		public bool CanSeePlayerBlips(string username)
        {
            return permissions.Find(p => p.name == username)?.canSeePlayerBlips ?? false;
        }
        public bool CanSeeLocomotives(string username)
        {
            return permissions.Find(p => p.name == username)?.canSeeLocomotives ?? false;
        }

        private void OnSessionStarted(string username)
        {
            if (!permissions.Any(p => p.name == username))
            {
                var clonedPermissions = defaultPermissions.ShallowCopy();
                clonedPermissions.name = username;

                permissions.Add(clonedPermissions);
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
			DrawLocoControlColumn();
			DrawSignalControlColumn();
			DrawPlayerBlipsColumn();
			DrawSeeLocomotivesColumn();
			GUILayout.EndHorizontal();
		}

        private void DrawColumn(string label, Action<PlayerPermissions> action)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(label);
            action(defaultPermissions);
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

		private void DrawLocoControlColumn()
		{
			DrawColumn("Locomotive Control", p => p.canControlLocomotives = GUILayout.Toggle(p.canControlLocomotives, ""));
		}

		private void DrawSignalControlColumn()
		{
			DrawColumn("Signal Control", p => p.canControlSignals = GUILayout.Toggle(p.canControlSignals, ""));
		}
		private void DrawPlayerBlipsColumn()
        {
            DrawColumn("Player Blips", p => p.canSeePlayerBlips = GUILayout.Toggle(p.canSeePlayerBlips, ""));
        }
        private void DrawSeeLocomotivesColumn()
        {
            DrawColumn("Locomotive Visibility", p => p.canSeeLocomotives = GUILayout.Toggle(p.canSeeLocomotives, ""));
        }
    }

    public class FeatureFlags
    {
        public bool enableSignals = false;

        public void Draw()
        {
            GUILayout.Label("Feature Flags:");
            GUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
            GUILayout.BeginVertical();
            enableSignals = GUILayout.Toggle(enableSignals, "Enable signals");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
    }
}
