using HarmonyLib;
using System;
using System.Reflection;
using BepInEx;
using UnityEngine.SceneManagement;

namespace DvMod.RemoteDispatch
{
    [BepInPlugin("com.github.mspielberg.dv-remote-dispatch", "RemoteDispatch", PluginInfo.PLUGIN_VERSION)]
    public class Main : BaseUnityPlugin
    {
        public static Main Instance = null!;
        public static Settings Settings = null!;
        private Harmony? harmony;

        private void Awake()
        {
            if (Instance != null)
            {
                Logger.LogFatal($"{Info.Metadata.Name} is already loaded!");
                Destroy(this);
                return;
            }

            Instance = this;

            Settings = new Settings(Config);

            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            harmony.PatchAll();

            WorldStreamingInit.LoadingFinished += () =>
            {
                if (Settings.startServerOnLoad.Value)
                    HttpServer.Create();
                Updater.Create();
                CarUpdater.Start();
            };
            
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (scene.buildIndex != (int)DVScenes.MainMenu)
                    return;
                HttpServer.Destroy();
                Updater.Destroy();
                CarUpdater.Stop();
            };
        }

        private void OnDisable()
        {
            CarUpdater.Stop();
            Updater.Destroy();
            HttpServer.Destroy();
            harmony?.UnpatchSelf();
        }

        public static void DebugLog(Func<string> message)
        {
            if (Settings.enableLogging.Value)
                Instance.Logger.LogDebug(message());
        }
    }
}
