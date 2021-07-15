using System.Linq;
using UnityModManagerNet;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Settings : UnityModManager.ModSettings
    {
        public int serverPort = 7245;
        public string serverPassword = "";
        public bool startServerOnLoad = false;
        public bool enableLogging = false;

        public readonly string? version = Main.mod?.Info.Version;

        const char EnDash = '\u2013';
        private string uncommittedPort = "initial";
        private string message = "";

        public void Draw()
        {
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
            enableLogging = GUILayout.Toggle(enableLogging, "Enable logging");
        }

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }
    }
}
