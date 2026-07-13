using DV.Logic.Job;
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
        public static bool PersistentJobsHooked;
        private static object[]? attachedJobChangedHandler;
        public static MethodInfo? PersJobsJobTrackChangedEventRegMethod;
        public static MethodInfo? PersJobsJobTrackChangedEventUnregMethod;
        public static FieldInfo? PersJobsSuspendedCarObjectsDict;
        public static FieldInfo? PersJobsSuspendedCarGUIDToJobChainControllerDict;
        public static FieldInfo? PersJobsTrainCarTypeToInterCouplerDistanceDict;

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

        private static void DisconnectFromPersistentJobs()
        {
            PersJobsJobTrackChangedEventUnregMethod?.Invoke(null, attachedJobChangedHandler);
            attachedJobChangedHandler = null;
            PersistentJobsHooked = false;
            DebugLog(() => "Persistent Jobs event handler removed"); 
        }

        private static void ConnectToPersistentJobs()
        {
            try
            {
                PersJobsJobTrackChangedEventRegMethod = AccessTools.Method(AccessTools.TypeByName("PersistentJobsMod.ModInteraction.PersistentJobsModInteractionFeatures"), "RegisterJobTracksChangedListener", new[] { typeof(Action<Job>) });
                PersJobsJobTrackChangedEventUnregMethod = AccessTools.Method(AccessTools.TypeByName("PersistentJobsMod.ModInteraction.PersistentJobsModInteractionFeatures"), "UnregisterJobTracksChangedListener", new[] { typeof(Action<Job>) });
                attachedJobChangedHandler = new[] {new Action<Job>(JobData.JobPatches.UpdateJobsFromPersistentJobs)};
                PersJobsJobTrackChangedEventRegMethod.Invoke(null, attachedJobChangedHandler);

                PersJobsSuspendedCarObjectsDict = AccessTools.Field(AccessTools.TypeByName("PersistentJobsMod.Optimization.FarCarOpt"), "SuspendedCarObjects");
                PersJobsSuspendedCarGUIDToJobChainControllerDict = AccessTools.Field(AccessTools.TypeByName("PersistentJobsMod.Optimization.FarCarOpt"), "SuspendedCarGUIDToJobChainController");
                PersJobsTrainCarTypeToInterCouplerDistanceDict = AccessTools.Field(AccessTools.TypeByName("PersistentJobsMod.Optimization.FarCarOpt"), "TrainCarTypeToInterCouplerDistance");
                
                PersistentJobsHooked = true;
                DebugLog(() => "Persistent Jobs found and hooked");
            }
            catch (Exception ex) 
            {
                Main.DebugLog(() => $"Couldn´t connect to Presistent Jobs - exception thrown: \n{ex}");
            }
        }

        private static void Start()
        {
            ConnectToPersistentJobs();
            HttpServer.Create();
            Updater.Create();
            CarUpdater.Start();
        }

        private static void Stop()
        {
            CarUpdater.Stop();
            Updater.Destroy();
            HttpServer.Destroy();
            DisconnectFromPersistentJobs();
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
