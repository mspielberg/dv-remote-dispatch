using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public class Transform { public Vector3 position, eulerAngles; public Vector3 InverseTransformPoint(Vector3 point) => new Vector3(); }
    public struct Bounds { public Vector3 min, max; }
    public static class Time { public static float unscaledTime; }
    public class Color { }
    public static class ColorUtility { public static string ToHtmlStringRGB(Color c) => "00FF00"; }
}
namespace HarmonyLib
{
    public class Harmony { public Harmony(string id) { } }
    public static class AccessTools { public static System.Reflection.FieldInfo? Field(Type type, string name) => type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); }
}
public class PointSet
{
    public class Point { public UnityEngine.Vector3 forward = new UnityEngine.Vector3(0, 0, 1); }
    public double span = 100;
    public Point[] points = new[] { new Point() };
    public int GetPointIndexForSpan(double span) => 0;
}
public class LogicTrack { public string ID = ""; }
public class RailTrack
{
    public string Id = "";
    public double Length = 100;
    public UnityEngine.Vector3 Forward = new UnityEngine.Vector3(0, 0, 1);
    public Junction? inJunction, outJunction;
    public Junction.Branch? inBranch, outBranch;
    public PointSet GetKinkedPointSet() => new PointSet { span = Length, points = new[] { new PointSet.Point { forward = Forward } } };
    public LogicTrack LogicTrack() => new LogicTrack { ID = Id };
    public List<Junction.Branch>? GetAllInBranches() => inJunction?.Next(this, true) ?? (inBranch == null ? null : new List<Junction.Branch> { inBranch });
    public List<Junction.Branch>? GetAllOutBranches() => outJunction?.Next(this, false) ?? (outBranch == null ? null : new List<Junction.Branch> { outBranch });
}
public class Junction
{
    public enum SwitchMode : byte { REGULAR, FORCED, NO_SOUND }
    public class Branch { public RailTrack track = null!; public bool first; }
    public Branch inBranch = null!;
    public List<Branch> outBranches = new List<Branch>();
    public byte selectedBranch;
    public UnityEngine.Vector3 position;
    public void Switch(SwitchMode mode, byte branch) { if (DvMod.RemoteDispatch.RouteManager.AllowSwitch(this, branch)) selectedBranch = branch; }
    public List<Branch>? Next(RailTrack track, bool first) => inBranch.track == track && inBranch.first == first ? outBranches : new List<Branch> { inBranch };
}
public class RailTrackRegistry
{
    public static RailTrackRegistry Instance = new RailTrackRegistry();
    public static RailTrack[] RailTracks = Array.Empty<RailTrack>();
    public Junction[] OrderedJunctions = Array.Empty<Junction>();
}
public class Traveller { public double Span; }
public class Bogie
{
    public bool HasDerailed;
    public bool fullyInitialized = true;
    public RailTrack? track;
    public Traveller? traveller = new Traveller();
    public UnityEngine.Transform transform = new UnityEngine.Transform();
}
public class Trainset { public List<TrainCar> cars = new List<TrainCar>(); }
public class TrainCar
{
    public string CarGUID = "";
    public Bogie[] Bogies = Array.Empty<Bogie>();
    public Trainset? trainset;
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public UnityEngine.Bounds Bounds;
}
public class TrainCarRegistry
{
    public static TrainCarRegistry Instance = new TrainCarRegistry();
    public Dictionary<string, TrainCar> logicCarToTrainCar = new Dictionary<string, TrainCar>();
}
public static class WorldMover { public static UnityEngine.Vector3 currentMove; }
namespace Signals.Common { public enum CrossingCheckMode { Ignore, WholeTrack, IntersectionOnly } }
namespace Signals.Game
{
    public enum TrackDirection { Out, In }
    public enum SignalType { NotSet, Mainline, Entry, Exit, ExitPax, ExitMainline, Spacing, Shunting, Distant, Repeater, Other }
    public struct SignalPlacementInfo { public RailTrack Track; public TrackDirection Direction; public double Span; }
    public class AspectDefinition { public bool UsePassingSpeed; public Lamp[] OnLights = Array.Empty<Lamp>(); }
    public class Lamp { public UnityEngine.Color Colour = new UnityEngine.Color(); }
    public class Aspect
    {
        public string Id = ""; public bool DisallowPassing;
        public AspectDefinition GetDefinition() => new AspectDefinition();
    }
    public class Definition { public UnityEngine.Transform transform = new UnityEngine.Transform(); }
    public class Signal
    {
        public int Id;
        public string Name = "";
        public bool IsShunting;
        public Signal? Parent, DistantSignal;
        public Controllers.BasicSignalController Controller = null!;
        public Aspect[] AllAspects = new[] { new Aspect { Id = "stop", DisallowPassing = true }, new Aspect { Id = "clear" } };
        public Railway.TrackBlock? Block;
        public Definition Definition = new Definition();
        public int CurrentAspectIndex;
        public bool ShuntingAllowed = true;
    }
    public class SignalManager
    {
        public static SignalManager Instance = new SignalManager();
        public static bool Running = true;
        public List<Controllers.BasicSignalController> AllControllers = new List<Controllers.BasicSignalController>();
    }
}
namespace Signals.Game.Controllers
{
    public class BasicSignalController
    {
        public Definition Definition = new Definition();
        public SignalPlacementInfo? PlacementInfo;
        public SignalType Type = SignalType.Mainline;
        public Signal[] Signals = Array.Empty<Signal>(), ShuntingSignals = Array.Empty<Signal>();
        public IEnumerable<Signal> AllSignals => Signals.Concat(ShuntingSignals);
        public void FlagAllBlocksForUpdating() { }
        public void UpdateBlocks() { }
        public Signal? GetControllerSignal() => Signals.FirstOrDefault();
    }
    public class TrackSignalController : BasicSignalController { }
    public class JunctionSignalController : TrackSignalController { public Junction Junction = null!; }
    public class DistantSignalController : BasicSignalController { public BasicSignalController Home = null!; }
}
namespace Signals.Game.Railway
{
    public class TrackBlock { public HashSet<RailTrack> AllTracks = new HashSet<RailTrack>(); }
    public static class TurntableHelper { public static bool IsTurntableEnd(RailTrack t) => false; }
    public static class TrackReserver
    {
        public static HashSet<RailTrack> Reserved = new HashSet<RailTrack>();
        public static bool IsTrackReserved(RailTrack track, out Signal by) { by = null!; return Reserved.Contains(track); }
    }
    public static class TrackChecker
    {
        public class TrackIntersectionPoints { public bool Busy; public bool TestIntersections() => Busy; }
        public static Dictionary<RailTrack, TrackIntersectionPoints> s_intersectionMap = new Dictionary<RailTrack, TrackIntersectionPoints>();
        public static HashSet<RailTrack> Occupied = new HashSet<RailTrack>();
        public static bool IsOccupied(RailTrack track, Signals.Common.CrossingCheckMode mode) => Occupied.Contains(track);
        public static HashSet<RailTrack> GetAllOccupiedTracks() => Occupied;
    }
}
namespace DvMod.RemoteDispatch
{
    public class Logger { public void Log(string s) { } public void Error(string s) => System.Console.WriteLine(s); }
    public class ModInfo { public string Id = "RemoteDispatch"; }
    public class ModEntry { public ModInfo Info = new ModInfo(); public Logger Logger = new Logger(); }
    public class TestSettings { public int auxiliaryReleaseSeconds = 90; public float routeClearanceMeters = 8; }
    public static class Main { public static TestSettings settings = new TestSettings(); public static ModEntry? mod = new ModEntry(); }
    public static class DispatchNetwork
    {
        public static bool Connected, Host = true, AdapterFailed;
        public static bool Authority => !Connected || Host;
        public static Action<string>? SendState;
    }
    public static class Sessions
    {
        public static readonly List<string> Tags = new List<string>();
        public static void AddTag(string s) => Tags.Add(s);
    }
    public static class World
    {
        public struct Position
        {
            private UnityEngine.Vector3 position;
            public Position(UnityEngine.Vector3 p) { position = p; }
            public Position ToLatLon() => this;
            public JArray ToJson() => new JArray(position.x, position.z);
        }
    }
    public static class SignalIntegration
    {
        public static bool Available = true;
        public static IEnumerable<Signals.Game.Signal> AllSignals => Signals.Game.SignalManager.Instance.AllControllers.SelectMany(c => c.AllSignals);
        public static void Install(HarmonyLib.Harmony h) { }
        public static void ReleaseControl() { }
        public static void Apply(Signals.Game.Signal s, int i) { s.CurrentAspectIndex = i; s.ShuntingAllowed = RouteManager.IsShuntingAllowed(s); }
        public static bool IsShuntingHead(Signals.Game.Signal s) => s.IsShunting;
        public static bool CanManualShunt(Signals.Game.Signal s) => s.Parent == null && s.Controller.PlacementInfo.HasValue;
        public static int ShuntingAspect(Signals.Game.Signal s) => s.IsShunting ? 1 : 0;
        public static int StopAspect(Signals.Game.Signal s) => 0;
        public static int Evaluate(Signals.Game.Signal s, bool shunting) => 1;
        public static bool IsSubstituteAspect(Signals.Game.Signal s, int index) => index >= 0 && index < s.AllAspects.Length && s.AllAspects[index].Id == "Sz";
        public static string AspectColour(Signals.Game.Signal s, int index) => index <= 0 ? "red" : "green";
        public static bool CanSelect(Signals.Game.Signal s, bool shunting) => s.Parent == null && s.Controller.PlacementInfo.HasValue && (shunting || !s.IsShunting);
    }
}
