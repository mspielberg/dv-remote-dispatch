using DV.Logic.Job;
using HarmonyLib;
using System;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    [EnableReloading]
    public static class Main
    {
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();
        public static bool enabled;

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
            Harmony harmony = new Harmony(modEntry.Info.Id);

            if (value)
            {
                harmony.PatchAll();
                WorldStreamingInit.LoadingFinished += Start;
                UnloadWatcher.UnloadRequested += Stop;
                if (WorldStreamingInit.Instance && WorldStreamingInit.IsLoaded)
                {
                    Start();
                }
                PatchPersistentJobs();
            }
            else
            {
                Stop();
                UnloadWatcher.UnloadRequested -= Stop;
                WorldStreamingInit.LoadingFinished -= Start;
                harmony.UnpatchAll(modEntry.Info.Id);
            }
            return true;
        }

        private static void PatchPersistentJobs()
        {
            Type? persistentJobsModInteractionFeaturesType = GetPersistentJobsInteractionType();
            if (persistentJobsModInteractionFeaturesType != null)
            {
                var jobTracksChanged = persistentJobsModInteractionFeaturesType.GetEvent("JobTracksChanged");
                jobTracksChanged.AddEventHandler(null, new Action<Job>(JobData.JobPatches.UpdateJobsFromPersistentJobs));
                DebugLog(() => "Persistent Jobs found and hooked");
            }
        }
        /// <summary>
        /// This will check to see if persistent jobs mod is currently installed and loaded
        /// </summary>
        /// <returns>
        /// Persistent Jobs mod event interface if available, null otherwise.
        /// </returns>
        private static Type? GetPersistentJobsInteractionType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "PersistentJobsModInteractionFeatures")
                    {
                        return type;
                    }
                }
            }
            return null;
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
