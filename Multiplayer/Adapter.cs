using HarmonyLib;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using System;
using System.IO;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch.Multiplayer
{
    public sealed class DispatchStatePacket : ISerializablePacket
    {
        public string Json = "";
        public void Serialize(BinaryWriter writer) => writer.Write(Json);
        public void Deserialize(BinaryReader reader)
        {
            Json = reader.ReadString();
            if (Json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Dispatch snapshot is too large.");
        }
    }

    public static class Adapter
    {
        private static IServer? server;
        private static IClient? client;
        private static bool registered;
        private static int clientGeneration;
        private static readonly Harmony harmony = new Harmony("RemoteDispatch.Multiplayer");
        public static void Initialise()
        {
            if (registered) return;
            MultiplayerAPI.Instance.SetModCompatibility("RemoteDispatch", MultiplayerCompatibility.All);
            var mpAssembly = UnityModManager.FindMod("Multiplayer").Assembly;
            var handler = AccessTools.Method(mpAssembly.GetType("Multiplayer.Networking.Managers.Server.NetworkServer"), "OnCommonChangeJunctionPacket");
            if (handler == null) throw new MissingMethodException("Multiplayer's authoritative junction handler was not found.");
            harmony.Patch(handler, prefix: new HarmonyMethod(typeof(Adapter), nameof(BeforeJunctionPacket)) { priority = Priority.First });
            registered = true;
            DispatchNetwork.IsConnected = () => MultiplayerAPI.Instance?.IsConnected == true;
            DispatchNetwork.IsHost = () => MultiplayerAPI.Instance?.IsHost == true;
            DispatchNetwork.SendState = Broadcast;
            DispatchNetwork.Shutdown = Shutdown;
            MultiplayerAPI.ServerStarted += ServerStarted;
            MultiplayerAPI.ClientStarted += ClientStarted;
            MultiplayerAPI.ServerStopped += ServerStopped;
            MultiplayerAPI.ClientStopped += ClientStopped;
            if (MultiplayerAPI.Server != null) ServerStarted(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null) ClientStarted(MultiplayerAPI.Client);
        }
        private static void ServerStarted(IServer instance)
        {
            if (ReferenceEquals(server, instance)) return;
            if (server != null) server.OnPlayerReady -= PlayerReady;
            server = instance;
            // Only the host originates state. Incoming snapshots from clients are ignored.
            server.RegisterSerializablePacket<DispatchStatePacket>((packet, sender) => { });
            server.OnPlayerReady += PlayerReady;
        }
        private static void ClientStarted(IClient instance)
        {
            if (ReferenceEquals(client, instance)) return;
            client = instance;
            clientGeneration++;
            client.RegisterSerializablePacket<DispatchStatePacket>(Receive);
        }
        private static void PlayerReady(IPlayer player)
        {
            // World data may arrive before signals finish generating. Subsequent snapshots refresh it.
            var currentServer = server;
            _ = Updater.RunOnMainThread(() => {
                if (registered && ReferenceEquals(server, currentServer))
                    currentServer?.SendSerializablePacketToPlayer(new DispatchStatePacket { Json = RouteManager.NetworkSnapshot() }, player);
            });
        }
        private static void Receive(DispatchStatePacket packet)
        {
            if (!registered || MultiplayerAPI.Instance?.IsHost == true) return;
            int generation = clientGeneration;
            _ = Updater.RunOnMainThread(() => {
                if (registered && generation == clientGeneration && MultiplayerAPI.Instance?.IsConnected == true)
                    RouteManager.ReceiveSnapshot(packet.Json);
            });
        }
        private static void Broadcast(string json)
        {
            if (server != null && MultiplayerAPI.Instance?.IsHost == true)
                server.SendSerializablePacketToAll(new DispatchStatePacket { Json = json }, excludeSelf: true);
        }
        private static bool BeforeJunctionPacket(object __0)
        {
            ushort id = (ushort)AccessTools.Property(__0.GetType(), "NetId").GetValue(__0);
            if (!MultiplayerAPI.Instance.TryGetObjectFromNetId(id, out Junction junction) ||
                !RouteManager.TryGetLock(junction, out byte branch)) return true;
            // Correct the packet before the host forwards it, and send a lock snapshot to the sender too.
            AccessTools.Property(__0.GetType(), "SelectedBranch").SetValue(__0, branch);
            AccessTools.Property(__0.GetType(), "Mode").SetValue(__0, (byte)Junction.SwitchMode.NO_SOUND);
            Broadcast(RouteManager.NetworkSnapshot());
            return true;
        }
        private static void ServerStopped()
        {
            if (server != null) server.OnPlayerReady -= PlayerReady;
            server = null;
        }
        private static void ClientStopped()
        {
            client = null;
            clientGeneration++;
            RouteManager.ResetRemote();
        }
        private static void Shutdown()
        {
            MultiplayerAPI.ServerStarted -= ServerStarted;
            MultiplayerAPI.ClientStarted -= ClientStarted;
            MultiplayerAPI.ServerStopped -= ServerStopped;
            MultiplayerAPI.ClientStopped -= ClientStopped;
            ServerStopped(); ClientStopped();
            harmony.UnpatchAll("RemoteDispatch.Multiplayer");
            registered = false;
        }
    }
}
