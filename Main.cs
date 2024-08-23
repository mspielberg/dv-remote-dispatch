using DV.Logic.Job;
using DvMod.RemoteDispatch.Patches.Game;
using HarmonyLib;
using System;
using System.Reflection;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    [EnableReloading]
    public static class Main
    {
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();
        public static bool enabled;
        private static Action<Job>? attachedJobChangedHandler;

        static public bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            try
            {
                var loaded = Settings.Load<Settings>(modEntry);
                if (loaded.version == modEntry.Info.Version)
                    settings = loaded;
            }
            catch
            {
            }

            mod.OnGUI = OnGUI;
            mod.OnSaveGUI = OnSaveGUI;
            mod.OnToggle = OnToggle;

            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Draw();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            //EndPointListenerPatch.CompareHost("[::]", "[2001:8003:a947:a00:ed03:2329:ddd4:de3c]");
            Harmony harmony = new Harmony(modEntry.Info.Id);

            if (value)
            {
                /*System.Uri uri = new System.Uri("google.com");
                Main.DebugLog(() => $"newUri: {uri.Host}");*/
                harmony.PatchAll();
                var methods = harmony.GetPatchedMethods();
                foreach (var method in methods)
                {
                    Main.DebugLog(() => $"patched: {method.DeclaringType?.FullName}.{method.Name}");
                }
                WorldStreamingInit.LoadingFinished += Start;
                UnloadWatcher.UnloadRequested += Stop;
                ConnectToPersistentJobs();
                if (WorldStreamingInit.Instance && WorldStreamingInit.IsLoaded)
                {
                    Start();
                }
            }
            else
            {
                Stop();
                UnloadWatcher.UnloadRequested -= Stop;
                WorldStreamingInit.LoadingFinished -= Start;
                DisconnectFromPersistentJobs();
                harmony.UnpatchAll(modEntry.Info.Id);
            }
            return true;
        }

        private static void DisconnectFromPersistentJobs()
        {
            EventInfo? jobTracksChanged = GetPersistentJobsTrackChangedEvent();
            if (jobTracksChanged != null && attachedJobChangedHandler != null)
            {
                jobTracksChanged.RemoveEventHandler(null, attachedJobChangedHandler);
                attachedJobChangedHandler = null;
                DebugLog(() => "Persistent Jobs event handler removed");
            }
        }

        private static void ConnectToPersistentJobs()
        {
            EventInfo? jobTracksChanged = GetPersistentJobsTrackChangedEvent();
            if (jobTracksChanged != null)
            {
                attachedJobChangedHandler = new Action<Job>(JobData.JobPatches.UpdateJobsFromPersistentJobs);
                jobTracksChanged.AddEventHandler(null, attachedJobChangedHandler);
                DebugLog(() => "Persistent Jobs found and hooked");
            }
        }

        /// <summary>Gets event exposed by Persistent Jobs for notifications when jobs are modified.</summary>
        /// <returns>Persistent Jobs mod track changed event if installed, otherwise null.</returns>
        private static EventInfo? GetPersistentJobsTrackChangedEvent()
        {
            return UnityModManager.FindMod("PersistentJobsMod")
                ?.Assembly
                ?.GetType("PersistentJobsModInteractionFeatures")
                ?.GetEvent("JobTracksChanged");
        }

        private static void Start()
        {
            HttpServer.Create();
            Updater.Create();
            CarUpdater.Start();
        }

        private static void Stop()
        {
            CarUpdater.Stop();
            Updater.Destroy();
            HttpServer.Destroy();
        }

        public static void DebugLog(TrainCar car, Func<string> message)
        {
            if (car == PlayerManager.Car)
                DebugLog(message);
        }

        public static void DebugLog(Func<string> message)
        {
            if (settings.enableLogging)
                mod?.Logger.Log(message());
        }
    }
}
