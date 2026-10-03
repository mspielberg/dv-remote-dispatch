using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    // The optional MPAPI adapter is loaded only when Multiplayer is present.
    public static class DispatchNetwork
    {
        public static Func<bool>? IsConnected;
        public static Func<bool>? IsHost;
        public static Action<string>? SendState;
        public static Action? Shutdown;
        public static bool Connected => IsConnected?.Invoke() ?? false;
        public static bool Authority => !Connected || (IsHost?.Invoke() ?? false);
        public static bool AdapterFailed { get; private set; }
        public static void Initialise()
        {
            if (UnityModManager.FindMod("Multiplayer")?.Active != true) return;
            try
            {
                var assembly = Assembly.LoadFrom(Path.Combine(Main.mod!.Path, "RemoteDispatch.Multiplayer.dll"));
                assembly.GetType("DvMod.RemoteDispatch.Multiplayer.Adapter", true)
                    .GetMethod("Initialise").Invoke(null, null);
            }
            catch (Exception e)
            {
                AdapterFailed = true;
                Main.mod?.Logger.Error("Multiplayer route integration could not load: " + e);
            }
        }
        public static void Stop()
        {
            Shutdown?.Invoke();
            Shutdown = null;
            IsConnected = null;
            IsHost = null;
            SendState = null;
            AdapterFailed = false;
        }
    }
}
