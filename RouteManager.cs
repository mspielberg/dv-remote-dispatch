using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using DvSignal = Signals.Game.Signal;

namespace DvMod.RemoteDispatch
{
    public sealed class DispatchException : Exception
    {
        public readonly int Status;
        public DispatchException(string message, int status = 409) : base(message) { Status = status; }
    }

    public static class RouteManager
    {
        internal static Func<DateTime> Clock = () => DateTime.UtcNow;
        private sealed class Draft
        {
            public string Id = Guid.NewGuid().ToString("N");
            public DvSignal Start = null!;
            public DvSignal End = null!;
            public bool Shunting;
            public RoutePath Path = null!;
            public double StartSpan, EndSpan, ReleasePoint;
            public DateTime Expires = Clock().AddMinutes(2);
            public readonly Dictionary<int, double> Signals = new Dictionary<int, double>();
            public readonly Dictionary<string, (double From, double To)> Ranges = new Dictionary<string, (double, double)>();
        }
        private sealed class ActiveRoute
        {
            public Draft Draft = null!;
            public string User = "";
            public string State = "set";
            public readonly RouteReleaseTracker Release = new RouteReleaseTracker();
            public readonly HashSet<string> Cars = new HashSet<string>();
            public readonly Dictionary<string, double> Previous = new Dictionary<string, double>();
            public readonly HashSet<string> PassedSignals = new HashSet<string>();
            public DateTime? ReleaseAt;
            public DateTime? ClearSince;
            public bool Faulted;
        }
        private sealed class Observation
        {
            public Bogie Bogie = null!;
            public TrainCar Car = null!;
            public string Key = "";
        }
        private static readonly Dictionary<string, RouteNode> graph = new Dictionary<string, RouteNode>();
        private static readonly Dictionary<string, RailTrack> tracks = new Dictionary<string, RailTrack>();
        private static readonly Dictionary<int, DvSignal> signals = new Dictionary<int, DvSignal>();
        private static readonly Dictionary<string, Draft> drafts = new Dictionary<string, Draft>();
        private static readonly List<ActiveRoute> routes = new List<ActiveRoute>();
        private static readonly Dictionary<int, byte> locks = new Dictionary<int, byte>();
        private static readonly Dictionary<int, int> aspects = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> remoteAspects = new Dictionary<int, int>();
        private static readonly Dictionary<int, byte> remoteLocks = new Dictionary<int, byte>();
        private static readonly HashSet<int> manualShunting = new HashSet<int>();
        private static readonly HashSet<int> shuntingStopped = new HashSet<int>();
        private static readonly HashSet<int> remoteShunting = new HashSet<int>();
        private static readonly HashSet<string> occupiedTracks = new HashSet<string>();
        private static JObject remoteState = new JObject();
        private static bool running, remoteControl, applyingSwitches;
        private static float nextTick, nextNetwork, nextIdleAspects, nextOccupancy;
        private static string lastState = "";
        private static int controllerCount = -1;
        private static BasicSignalController? firstController;
        private static string failure = "";
        private static long sequence;
        private static string epoch = Guid.NewGuid().ToString("N");
        private static string remoteEpoch = "";
        private static long remoteSequence = -1;
        private static bool aspectsChanged;
        private static string layout = "";
        private static string? pendingSnapshot;
        private static bool remoteMismatch;
        public static bool Controlling => running && signals.Count > 0 &&
            (DispatchNetwork.Authority || remoteControl || (DispatchNetwork.Connected && remoteEpoch.Length == 0));
        public static bool HasLocks => routes.Count > 0 || remoteLocks.Count > 0 || ((JArray?)remoteState["routes"])?.Count > 0;
        public static event Action? StateChanged;
        public static void Start()
        {
            running = true;
            epoch = Guid.NewGuid().ToString("N");
            sequence = 0;
            lastState = "";
            controllerCount = -1;
            firstController = null;
            layout = ""; pendingSnapshot = null; remoteMismatch = false;
            failure = "";
            nextTick = nextNetwork = nextIdleAspects = nextOccupancy = 0;
        }
        public static void Stop()
        {
            running = false;
            SignalIntegration.ReleaseControl();
            remoteControl = false;
            locks.Clear(); remoteLocks.Clear(); aspects.Clear(); remoteAspects.Clear();
            routes.Clear(); drafts.Clear(); signals.Clear(); graph.Clear(); tracks.Clear();
            manualShunting.Clear(); shuntingStopped.Clear(); remoteShunting.Clear(); occupiedTracks.Clear();
            remoteState = new JObject(); remoteEpoch = ""; remoteSequence = -1;
            layout = ""; pendingSnapshot = null; remoteMismatch = false;
            Sessions.AddTag("signals"); Sessions.AddTag("routes");
            DispatchNetwork.SendState?.Invoke(NetworkSnapshot());
        }
        public static void ResetRemote()
        {
            remoteControl = false;
            remoteEpoch = ""; remoteSequence = -1;
            pendingSnapshot = null; remoteMismatch = false;
            remoteLocks.Clear(); remoteAspects.Clear(); remoteState = new JObject();
            remoteShunting.Clear();
            Sessions.AddTag("signals"); Sessions.AddTag("routes");
        }
        public static bool TryGetLock(Junction junction, out byte branch)
        {
            if (!DispatchNetwork.Authority && remoteMismatch) { branch = junction.selectedBranch; return true; }
            int id = Array.IndexOf(RailTrackRegistry.Instance.OrderedJunctions, junction);
            return (DispatchNetwork.Authority ? locks : remoteLocks).TryGetValue(id, out branch);
        }
        public static bool AllowSwitch(Junction junction, byte branch)
        {
            if (applyingSwitches) return true;
            return !TryGetLock(junction, out var locked) ||
                (junction.outBranches.Count > 0 && branch % junction.outBranches.Count == locked);
        }
        private static void EnsureAuthority(bool allowFault = false)
        {
            if (!DispatchNetwork.Authority) throw new DispatchException("Connect your browser to the multiplayer host's Remote Dispatch.", 403);
            if (DispatchNetwork.AdapterFailed) throw new DispatchException("Multiplayer integration failed to load; route control is unavailable.", 503);
            if (!running || !SignalIntegration.Available || signals.Count == 0)
                throw new DispatchException("DV Signals is not ready.", 503);
            if (!allowFault && !string.IsNullOrEmpty(failure)) throw new DispatchException(failure, 503);
        }
        private static string TrackId(RailTrack track) => track.LogicTrack().ID.ToString();
        private static string NodeId(string track, TrackDirection dir) => track + (dir == TrackDirection.Out ? "/out" : "/in");
        // DV Signals placement describes where the signal FACES, towards the approaching train.
        // A train passing that signal travels in the opposite direction.
        private static TrackDirection TravelDirection(SignalPlacementInfo p) => p.Direction == TrackDirection.Out ? TrackDirection.In : TrackDirection.Out;
        private static double DirectedSpan(SignalPlacementInfo p) => TravelDirection(p) == TrackDirection.Out ? p.Span : p.Track.GetKinkedPointSet().span - p.Span;
        internal static double SignalRotation(DvSignal signal)
        {
            var placement = signal.Controller.PlacementInfo;
            if (!placement.HasValue) return (signal.Controller.Definition.transform.eulerAngles.y + 180) % 360;
            var p = placement.Value;
            var points = p.Track.GetKinkedPointSet();
            var forward = points.points[points.GetPointIndexForSpan(p.Span)].forward;
            double sign = TravelDirection(p) == TrackDirection.Out ? 1 : -1;
            return Math.Round((Math.Atan2(sign * forward.x, sign * forward.z) * 180 / Math.PI + 360) % 360, 2);
        }
        private static double BogieSpan(Bogie b, TrackDirection direction) => direction == TrackDirection.Out ? b.traveller!.Span : b.track!.GetKinkedPointSet().span - b.traveller!.Span;

        private static void BuildGraph()
        {
            graph.Clear(); tracks.Clear(); drafts.Clear();
            foreach (var track in RailTrackRegistry.RailTracks)
            {
                string id = TrackId(track);
                tracks[id] = track;
                foreach (TrackDirection dir in new[] { TrackDirection.Out, TrackDirection.In })
                {
                    var node = new RouteNode { Id = NodeId(id, dir), Track = id, Length = track.GetKinkedPointSet().span };
                    var junction = dir == TrackDirection.Out ? track.outJunction : track.inJunction;
                    var branches = dir == TrackDirection.Out ? track.GetAllOutBranches() : track.GetAllInBranches();
                    if (branches != null)
                        foreach (var branch in branches.Where(b => b?.track != null))
                        {
                            // Turntables are movable topology and cannot be secured by a switch lock.
                            if (TurntableHelper.IsTurntableEnd(track) || TurntableHelper.IsTurntableEnd(branch.track)) continue;
                            int jid = junction == null ? -1 : Array.IndexOf(RailTrackRegistry.Instance.OrderedJunctions, junction);
                            int index = junction == null ? -1 : junction.outBranches.FindIndex(b => b.track == branch.track);
                            if (junction != null && index < 0) index = junction.outBranches.FindIndex(b => b.track == track);
                            if (junction != null && (jid < 0 || index < 0)) continue;
                            node.Edges.Add(new RouteEdge {
                                Target = NodeId(TrackId(branch.track), branch.first ? TrackDirection.Out : TrackDirection.In),
                                Junction = jid, Branch = (byte)Math.Max(0, index)
                            });
                        }
                    graph[node.Id] = node;
                }
            }
        }
        private static string LayoutHash()
        {
            string topology = string.Join("|", graph.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                p.Key + ":" + Math.Round(p.Value.Length, 2).ToString(CultureInfo.InvariantCulture) + ":" +
                string.Join(",", p.Value.Edges.OrderBy(e => e.Target, StringComparer.Ordinal).Select(e => e.Target + "@" + e.Junction + ":" + e.Branch))));
            string heads = string.Join("|", signals.OrderBy(p => p.Key).Select(p =>
                p.Key + ":" + p.Value.Name + ":" + string.Join(",", p.Value.AllAspects.Select(a => a.Id)) + ":" +
                (p.Value.Controller.PlacementInfo.HasValue ? TrackId(p.Value.Controller.PlacementInfo.Value.Track) + ":" +
                    p.Value.Controller.PlacementInfo.Value.Direction + ":" +
                    Math.Round(p.Value.Controller.PlacementInfo.Value.Span, 2).ToString(CultureInfo.InvariantCulture) : "")));
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(topology + "\n" + heads))).Replace("-", "");
        }
        private static IEnumerable<Observation> ObserveBogies()
        {
            foreach (var car in TrainCarRegistry.Instance.logicCarToTrainCar.Values.ToArray())
            {
                if (car == null) continue;
                for (int i = 0; i < car.Bogies.Length; i++)
                    if (car.Bogies[i] != null)
                        yield return new Observation { Car = car, Bogie = car.Bogies[i], Key = car.CarGUID + "/" + i };
            }
        }
        private static double Position(Draft draft, Bogie bogie)
        {
            if (bogie.HasDerailed || bogie.track == null || bogie.traveller == null) return double.NaN;
            string track = TrackId(bogie.track);
            double offset = -draft.StartSpan;
            foreach (string nodeId in draft.Path.Nodes)
            {
                var node = graph[nodeId];
                var dir = nodeId.EndsWith("/out", StringComparison.Ordinal) ? TrackDirection.Out : TrackDirection.In;
                if (node.Track == track) return offset + BogieSpan(bogie, dir);
                offset += node.Length;
            }
            // Include the tracks immediately beyond the destination for the rear overhang clearance.
            var last = graph[draft.Path.Nodes.Last()];
            foreach (var edge in last.Edges)
                if (graph.TryGetValue(edge.Target, out var next) && next.Track == track)
                {
                    var dir = edge.Target.EndsWith("/out", StringComparison.Ordinal) ? TrackDirection.Out : TrackDirection.In;
                    return offset + BogieSpan(bogie, dir);
                }
            return double.NaN;
        }
        private static bool Inside(Draft draft, Bogie bogie)
        {
            double p = Position(draft, bogie);
            return !double.IsNaN(p) && p >= 0 && p <= draft.Path.Length;
        }
        private static double Clearance(TrainCar car)
        {
            var bounds = car.Bounds;
            var centers = car.Bogies.Select(b => car.transform.InverseTransformPoint(b.transform.position).z).ToArray();
            if (centers.Length == 0) return Main.settings.routeClearanceMeters;
            double overhang = Math.Max(bounds.max.z - centers.Max(), centers.Min() - bounds.min.z);
            return Math.Max(Main.settings.routeClearanceMeters, overhang + 2);
        }
        private static bool NearJunction(Junction junction, IEnumerable<Observation> observations) =>
            observations.Any(o => (o.Bogie.transform.position - junction.position).sqrMagnitude < Math.Pow(Math.Max(15, Clearance(o.Car) + 3), 2));
        private static void UpdateOccupancy(Observation[] observations)
        {
            occupiedTracks.Clear();
            foreach (var track in observations.Select(o => o.Bogie.track).Where(t => t != null).Concat(TrackChecker.GetAllOccupiedTracks()))
                occupiedTracks.Add(TrackId(track!));
            foreach (var o in observations.Where(o => o.Bogie.track != null && o.Bogie.traveller != null))
            {
                var track = o.Bogie.track!;
                double span = o.Bogie.traveller!.Span, margin = Clearance(o.Car);
                occupiedTracks.UnionWith(BodyTracks(track, TrackDirection.In, span, margin));
                occupiedTracks.UnionWith(BodyTracks(track, TrackDirection.Out, track.GetKinkedPointSet().span - span, margin));
            }
            // A short track between a car's bogies is occupied even when neither bogie is on it.
            foreach (var car in observations.GroupBy(o => o.Car))
            {
                var bogies = car.Where(o => o.Bogie.track != null && o.Bogie.traveller != null).ToArray();
                if (bogies.Length < 2 || bogies[0].Bogie.track == bogies[1].Bogie.track) continue;
                var first = bogies[0].Bogie;
                string target = TrackId(bogies[1].Bogie.track!);
                double bodyLength = Math.Max(24, car.Key.Bounds.max.z - car.Key.Bounds.min.z + 4);
                foreach (var direction in new[] { TrackDirection.In, TrackDirection.Out })
                {
                    var covered = BodyTracks(first.track!, direction, direction == TrackDirection.In ? first.traveller!.Span :
                        first.track!.GetKinkedPointSet().span - first.traveller!.Span, bodyLength, target);
                    if (covered.Contains(target)) { occupiedTracks.UnionWith(covered); break; }
                }
            }
        }
        private static List<string> BodyTracks(RailTrack track, TrackDirection direction, double toBoundary, double extent, string? target = null)
        {
            var covered = new List<string>();
            string nodeId = NodeId(TrackId(track), direction);
            double remaining = extent - toBoundary;
            var visited = new HashSet<string> { TrackId(track) };
            while (remaining > 0 && covered.Count < 32 && graph.TryGetValue(nodeId, out var node))
            {
                var edge = node.Edges.FirstOrDefault(e => e.Junction < 0 || RailTrackRegistry.Instance.OrderedJunctions[e.Junction].selectedBranch == e.Branch);
                if (edge == null || !graph.TryGetValue(edge.Target, out var next) || !visited.Add(next.Track)) break;
                covered.Add(next.Track);
                if (next.Track == target) break;
                remaining -= next.Length; nodeId = next.Id;
            }
            return covered;
        }
        public static bool IsShuntingAllowed(DvSignal signal) => Controlling && failure.Length == 0 && !remoteMismatch &&
            (DispatchNetwork.Authority ? !shuntingStopped.Contains(signal.Id) && (manualShunting.Contains(signal.Id) || routes.Any(r =>
                !r.Faulted && !r.ReleaseAt.HasValue && r.Draft.Signals.ContainsKey(signal.Id) &&
                (r.Draft.Shunting || SignalIntegration.IsShuntingHead(signal)) && !r.PassedSignals.Contains(signal.Id.ToString()))) : remoteShunting.Contains(signal.Id));
        public static JObject SetManualShunting(int id, bool allowed, string username)
        {
            EnsureAuthority();
            if (!signals.TryGetValue(id, out var signal) || !SignalIntegration.CanManualShunt(signal))
                throw new DispatchException("Select a main signal or shunting signal.", 400);
            if (allowed) { manualShunting.Add(id); shuntingStopped.Remove(id); }
            else { manualShunting.Remove(id); shuntingStopped.Add(id); }
            // Explicit signal permission: no path search, occupancy check or switch operation.
            RefreshAspects(); Sessions.AddTag("signalStates"); Publish();
            Main.mod?.Logger.Log($"{(allowed ? "Rangierfahrt erlaubt" : "Rangierhalt")}: {signal.Name}, dispatcher {username}");
            return SignalState(signal);
        }

        private static void Describe(Draft draft)
        {
            double offset = -draft.StartSpan;
            for (int i = 0; i < draft.Path.Nodes.Count; i++)
            {
                var node = graph[draft.Path.Nodes[i]];
                if (i + 1 < draft.Path.Nodes.Count && node.Edges.Any(e => e.Target == draft.Path.Nodes[i + 1] && e.Junction >= 0))
                    draft.ReleasePoint = Math.Max(draft.ReleasePoint, offset + node.Length);
                double from = i == 0 ? draft.StartSpan : 0;
                double to = i == draft.Path.Nodes.Count - 1 ? draft.EndSpan : node.Length;
                var dir = draft.Path.Nodes[i].EndsWith("/out", StringComparison.Ordinal) ? TrackDirection.Out : TrackDirection.In;
                draft.Ranges[node.Track] = dir == TrackDirection.Out ? (from, to) : (node.Length - to, node.Length - from);
                foreach (var signal in signals.Values)
                {
                    // Standalone shunting heads on a main route must permit the train to pass too.
                    bool throughShunting = !draft.Shunting && SignalIntegration.IsShuntingHead(signal) && SignalIntegration.CanManualShunt(signal);
                    if ((!SignalIntegration.CanSelect(signal, draft.Shunting) && !throughShunting) || signal.Id == draft.End.Id) continue;
                    var placement = signal.Controller.PlacementInfo!.Value;
                    if (NodeId(TrackId(placement.Track), TravelDirection(placement)) != node.Id) continue;
                    double position = offset + DirectedSpan(placement);
                    if (position < -0.1 || position >= draft.Path.Length - 0.1) continue;
                    // Multi-head junction signals only authorize the head for the selected branch.
                    if (signal.Controller is JunctionSignalController jc)
                    {
                        int jid = Array.IndexOf(RailTrackRegistry.Instance.OrderedJunctions, jc.Junction);
                        var array = jc.ShuntingSignals.Contains(signal) ? jc.ShuntingSignals : jc.Signals;
                        if (array.Length > 1 && draft.Path.Switches.TryGetValue(jid, out var branch) &&
                            Array.IndexOf(array, signal) != branch % array.Length) continue;
                    }
                    draft.Signals[signal.Id] = Math.Max(0, position);
                }
                offset += node.Length;
            }
            if (!draft.Signals.ContainsKey(draft.Start.Id))
                throw new DispatchException("The selected signal head does not authorize this branch.");
        }
        public static JObject Preview(int startId, int endId, bool shunting)
        {
            EnsureAuthority();
            if (!signals.TryGetValue(startId, out var start) || !signals.TryGetValue(endId, out var end) || startId == endId ||
                !SignalIntegration.CanSelect(start, shunting) || !SignalIntegration.CanSelect(end, shunting))
                throw new DispatchException("Select two distinct signals facing the route, of the selected route type.", 400);
            var a = start.Controller.PlacementInfo!.Value;
            var b = end.Controller.PlacementInfo!.Value;
            var paths = RoutePlanning.FindPaths(graph, NodeId(TrackId(a.Track), TravelDirection(a)), NodeId(TrackId(b.Track), TravelDirection(b)),
                DirectedSpan(a), DirectedSpan(b), out var truncated);
            var candidates = new JArray();
            var observations = ObserveBogies().ToArray();
            UpdateOccupancy(observations);
            foreach (var path in paths)
            {
                var draft = new Draft { Start = start, End = end, Shunting = shunting, Path = path, StartSpan = DirectedSpan(a), EndSpan = DirectedSpan(b) };
                try { Describe(draft); }
                catch (DispatchException) { continue; }
                drafts[draft.Id] = draft;
                var json = DraftJson(draft);
                json["unavailable"] = CheckRoute(draft, observations);
                candidates.Add(json);
            }
            foreach (var key in drafts.Where(p => p.Value.Expires < Clock()).Select(p => p.Key).ToArray()) drafts.Remove(key);
            return new JObject { ["candidates"] = candidates, ["truncated"] = truncated };
        }
        private static bool Overlap((double From, double To) a, (double From, double To) b) => Math.Min(a.To, b.To) - Math.Max(a.From, b.From) > 0.5;
        private static string? CheckRoute(Draft draft, Observation[] observations)
        {
            foreach (var route in routes)
            {
                if (route.Draft.Path.Switches.Keys.Any(draft.Path.Switches.ContainsKey) ||
                    route.Draft.Ranges.Any(p => draft.Ranges.TryGetValue(p.Key, out var range) && Overlap(p.Value, range)) ||
                    route.Draft.Signals.Keys.Any(draft.Signals.ContainsKey))
                    return "Conflicts with route " + route.Draft.Id.Substring(0, 6) + ".";
                // Guard the fouling tracks of each junction, even when the routes don't share the chosen branch.
                foreach (int jid in draft.Path.Switches.Keys)
                {
                    var junction = RailTrackRegistry.Instance.OrderedJunctions[jid];
                    if (junction.outBranches.Select(b => TrackId(b.track)).Any(route.Draft.Ranges.ContainsKey))
                        return "Another route reserves a branch of J-" + jid + ".";
                }
                foreach (int jid in route.Draft.Path.Switches.Keys)
                {
                    var junction = RailTrackRegistry.Instance.OrderedJunctions[jid];
                    if (junction.outBranches.Select(b => TrackId(b.track)).Any(draft.Ranges.ContainsKey))
                        return "J-" + jid + " is protected by another route.";
                }
            }
            foreach (var pair in draft.Path.Switches)
            {
                var junction = RailTrackRegistry.Instance.OrderedJunctions[pair.Key];
                if (junction.selectedBranch != pair.Value && NearJunction(junction, observations))
                    return "J-" + pair.Key + " is occupied and cannot be moved.";
            }
            foreach (string id in draft.Ranges.Keys)
            {
                var track = tracks[id];
                if (TrackReserver.IsTrackReserved(track, out _)) return "Track " + id + " already has a DV Signals reservation.";
                bool isEntranceTrack = id == TrackId(draft.Start.Controller.PlacementInfo!.Value.Track);
                if (!draft.Shunting && ((!isEntranceTrack && occupiedTracks.Contains(id)) || observations.Any(o => o.Bogie.track == track &&
                        (!isEntranceTrack || Position(draft, o.Bogie) >= 0)) ||
                    TrackChecker.GetAllOccupiedTracks().Contains(track) || CrossingOccupied(track) ||
                    (!observations.Any(o => o.Bogie.track == track) && TrackChecker.IsOccupied(track, CrossingCheckMode.IntersectionOnly))))
                    return "Track " + id + " is occupied.";
            }
            return null;
        }
        private static bool CrossingOccupied(RailTrack track)
        {
            // DV Signals' general occupancy method includes the whole track. Read its existing crossing
            // geometry separately so an approaching train behind the entrance doesn't mask a crossing.
            var map = AccessTools.Field(typeof(TrackChecker), "s_intersectionMap")?.GetValue(null) as
                Dictionary<RailTrack, TrackChecker.TrackIntersectionPoints>;
            return map != null && map.TryGetValue(track, out var crossing) && crossing.TestIntersections();
        }
        public static JObject Establish(string candidateId, string username)
        {
            EnsureAuthority();
            if (!drafts.TryGetValue(candidateId, out var draft) || draft.Expires < Clock())
                throw new DispatchException("This path preview expired; select the two signals again.");
            var observations = ObserveBogies().ToArray();
            UpdateOccupancy(observations);
            string? error = CheckRoute(draft, observations);
            if (error != null) throw new DispatchException(error);
            // Reserve first, then align on the Unity thread. No HTTP request can interleave this transaction.
            var original = draft.Path.Switches.Keys.ToDictionary(id => id, id => RailTrackRegistry.Instance.OrderedJunctions[id].selectedBranch);
            var route = new ActiveRoute { Draft = draft, User = username };
            routes.Add(route);
            foreach (var pair in draft.Path.Switches) locks.Add(pair.Key, pair.Value);
            try
            {
                DispatchNetwork.SendState?.Invoke(NetworkSnapshot());
                applyingSwitches = true;
                foreach (var pair in draft.Path.Switches)
                {
                    var junction = RailTrackRegistry.Instance.OrderedJunctions[pair.Key];
                    if (junction.selectedBranch != pair.Value) junction.Switch(Junction.SwitchMode.REGULAR, pair.Value);
                    if (junction.selectedBranch != pair.Value) throw new DispatchException("J-" + pair.Key + " could not be aligned.");
                }
                foreach (var signal in signals.Values) signal.Controller.FlagAllBlocksForUpdating();
                foreach (var controller in signals.Values.Select(s => s.Controller).Distinct()) controller.UpdateBlocks();
                foreach (var observation in observations) route.Previous[observation.Key] = Position(draft, observation.Bogie);
                foreach (int id in draft.Signals.Keys) { manualShunting.Remove(id); shuntingStopped.Remove(id); }
                Sessions.AddTag("signalStates");
                drafts.Remove(candidateId);
            }
            catch
            {
                routes.Remove(route);
                foreach (int id in draft.Path.Switches.Keys) locks.Remove(id);
                foreach (var pair in original) RailTrackRegistry.Instance.OrderedJunctions[pair.Key].Switch(Junction.SwitchMode.REGULAR, pair.Value);
                RefreshAspects(); Publish();
                throw;
            }
            finally { applyingSwitches = false; }
            Main.mod?.Logger.Log($"Route {draft.Id}: {draft.Start.Name} -> {draft.End.Name}, {(draft.Shunting ? "Rangierfahrstraße" : "Fahrstraße")}, dispatcher {username}");
            RefreshAspects(); Publish();
            return RouteJson(route);
        }
        public static void Cancel(string id, bool auxiliary, string username)
        {
            if (auxiliary)
            {
                if (!DispatchNetwork.Authority) throw new DispatchException("Connect your browser to the multiplayer host's Remote Dispatch.", 403);
                if (!running) throw new DispatchException("Remote Dispatch is not running.", 503);
            }
            else EnsureAuthority();
            var route = routes.FirstOrDefault(r => r.Draft.Id == id) ?? throw new DispatchException("Route does not exist.", 404);
            if (!auxiliary)
            {
                if (route.Release.Entered || route.State == "releasing" || ObserveBogies().Any(o => Inside(route.Draft, o.Bogie)))
                    throw new DispatchException("This route is occupied or has been entered. Use Hilfsauflösung.");
                Remove(route);
            }
            else
            {
                if (route.ReleaseAt.HasValue) throw new DispatchException("Hilfsauflösung is already counting down.");
                foreach (int signalId in route.Draft.Signals.Keys) { manualShunting.Remove(signalId); shuntingStopped.Add(signalId); }
                route.State = "releasing";
                route.ReleaseAt = Clock().AddSeconds(Math.Max(1, Main.settings.auxiliaryReleaseSeconds));
            }
            Main.mod?.Logger.Log($"{(auxiliary ? "Hilfsauflösung requested" : "Route cancelled")}: {id}, dispatcher {username}");
            if (SignalIntegration.Available) RefreshAspects();
            Publish();
        }
        private static void Remove(ActiveRoute route)
        {
            routes.Remove(route);
            nextIdleAspects = 0;
            foreach (int id in route.Draft.Path.Switches.Keys) locks.Remove(id);
            Main.mod?.Logger.Log("Route released: " + route.Draft.Id);
        }
        private static void TrackTrain(ActiveRoute route, Observation[] observations)
        {
            var draft = route.Draft;
            foreach (var observation in observations)
            {
                var bogie = observation.Bogie;
                double p = Position(draft, bogie);
                if (route.Previous.TryGetValue(observation.Key, out var previous) && previous < 0 && p >= 0 && p <= draft.Path.Length)
                {
                    var consist = observation.Car.trainset?.cars ?? new List<TrainCar> { observation.Car };
                    foreach (var car in consist) route.Cars.Add(car.CarGUID);
                    route.Release.Capture(observations.Where(o => route.Cars.Contains(o.Car.CarGUID)).Select(o => o.Key));
                    if (route.State == "set") route.State = "occupied";
                }
                if (route.Cars.Contains(observation.Car.CarGUID))
                {
                    // Keep every originally captured car after uncoupling, and add any newly coupled cars.
                    if (observation.Car.trainset != null)
                        foreach (var car in observation.Car.trainset.cars) route.Cars.Add(car.CarGUID);
                    bool valid = bogie.track != null && !bogie.HasDerailed && bogie.fullyInitialized;
                    if (!valid)
                    {
                        route.Faulted = true;
                        if (!route.ReleaseAt.HasValue) route.State = "fault";
                    }
                    double margin = Clearance(observation.Car);
                    bool beyond = !double.IsNaN(p) ? p > draft.ReleasePoint + margin :
                        route.Previous.TryGetValue(observation.Key, out var last) && last > draft.ReleasePoint + margin;
                    route.Release.Observe(observation.Key, beyond, valid);
                    foreach (var signal in draft.Signals)
                        if (!double.IsNaN(p) && p >= signal.Value) route.PassedSignals.Add(signal.Key.ToString());
                }
                // Preserve confirmed switch clearance on farther tracks; reversing back into the route resets it.
                if (!double.IsNaN(p) || !route.Previous.TryGetValue(observation.Key, out var old) || old <= draft.ReleasePoint + Clearance(observation.Car))
                    route.Previous[observation.Key] = p;
            }
            if (route.Release.Entered)
            {
                route.Release.Capture(observations.Where(o => route.Cars.Contains(o.Car.CarGUID)).Select(o => o.Key));
                var observed = new HashSet<string>(observations.Select(o => o.Key));
                foreach (string key in route.Release.BogieIds.ToArray())
                    if (!observed.Contains(key))
                    {
                        route.Release.Observe(key, false, false);
                        route.Faulted = true;
                        if (!route.ReleaseAt.HasValue) route.State = "fault";
                    }
            }
            bool clear = !draft.Path.Switches.Keys.Any(id => NearJunction(RailTrackRegistry.Instance.OrderedJunctions[id], observations));
            if (!route.Faulted && route.Release.CanRelease(clear))
            {
                route.ClearSince ??= Clock();
                if ((Clock() - route.ClearSince.Value).TotalSeconds >= 2 && !route.ReleaseAt.HasValue) Remove(route);
            }
            else route.ClearSince = null;
        }
        public static void Tick()
        {
            if (!running || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.1f;
            try
            {
                // Recovery must still complete if the signal mod stops or its layout fails.
                if (DispatchNetwork.Authority)
                {
                    var expired = routes.Where(r => r.ReleaseAt.HasValue && Clock() >= r.ReleaseAt.Value).ToArray();
                    foreach (var route in expired) Remove(route);
                    if (expired.Length > 0) Publish();
                }
                if (!SignalIntegration.Available) return;
                SignalIntegration.Install(new Harmony(Main.mod!.Info.Id));
                if (controllerCount != SignalManager.Instance.AllControllers.Count ||
                    !ReferenceEquals(firstController, SignalManager.Instance.AllControllers.FirstOrDefault()))
                {
                    if (routes.Count > 0) { failure = "Signals changed while routes were locked. Use Hilfsauflösung before reloading signals."; }
                    else BuildGraph();
                    signals.Clear();
                    manualShunting.Clear(); shuntingStopped.Clear();
                    foreach (var signal in SignalIntegration.AllSignals) signals[signal.Id] = signal;
                    controllerCount = SignalManager.Instance.AllControllers.Count;
                    firstController = SignalManager.Instance.AllControllers.FirstOrDefault();
                    layout = LayoutHash();
                    Sessions.AddTag("signals");
                    nextIdleAspects = 0;
                    if (pendingSnapshot != null)
                    {
                        var snapshot = pendingSnapshot;
                        pendingSnapshot = null;
                        ReceiveSnapshot(snapshot);
                    }
                }
                if (DispatchNetwork.Authority)
                {
                    var observations = routes.Count > 0 || Time.unscaledTime >= nextOccupancy ? ObserveBogies().ToArray() : Array.Empty<Observation>();
                    if (routes.Count > 0 || Time.unscaledTime >= nextOccupancy) { UpdateOccupancy(observations); nextOccupancy = Time.unscaledTime + .25f; }
                    foreach (var route in routes.ToArray())
                    {
                        if (route.ReleaseAt.HasValue && Clock() >= route.ReleaseAt.Value) { Remove(route); continue; }
                        TrackTrain(route, observations);
                        if (route.Draft.Path.Switches.Any(p => RailTrackRegistry.Instance.OrderedJunctions[p.Key].selectedBranch != p.Value))
                        {
                            route.Faulted = true;
                            if (!route.ReleaseAt.HasValue) route.State = "fault";
                            foreach (var pair in route.Draft.Path.Switches)
                            {
                                var junction = RailTrackRegistry.Instance.OrderedJunctions[pair.Key];
                                if (!NearJunction(junction, observations))
                                    junction.Switch(Junction.SwitchMode.NO_SOUND, pair.Value);
                            }
                        }
                    }
                    if (routes.Count > 0 || Time.unscaledTime >= nextIdleAspects)
                    {
                        RefreshAspects();
                        nextIdleAspects = Time.unscaledTime + 1;
                    }
                    if (GetState().ToString(Formatting.None) != lastState || aspectsChanged) Publish();
                    if (Time.unscaledTime >= nextNetwork)
                    {
                        nextNetwork = Time.unscaledTime + 2;
                        DispatchNetwork.SendState?.Invoke(NetworkSnapshot());
                    }
                }
                else foreach (var signal in signals.Values) SignalIntegration.Apply(signal, GetAspect(signal));
            }
            catch (Exception e)
            {
                if (failure.Length == 0) Main.mod?.Logger.Error("Route control failed; signals held at stop: " + e);
                failure = "Route control error. Signals are held at stop; see the mod log.";
                foreach (var signal in signals.Values) SignalIntegration.Apply(signal, SignalIntegration.StopAspect(signal));
            }
        }
        private static void RefreshAspects()
        {
            var previous = new Dictionary<int, int>(aspects);
            var previousShunting = new HashSet<int>(signals.Values.Where(s => s.ShuntingAllowed).Select(s => s.Id));
            // Calculate from destination backwards so distant and speed indications see the upcoming stop.
            foreach (var signal in signals.Values) aspects[signal.Id] = manualShunting.Contains(signal.Id) && failure.Length == 0 ?
                SignalIntegration.ShuntingAspect(signal) : SignalIntegration.StopAspect(signal);
            foreach (var route in routes)
                foreach (var item in route.Draft.Signals.OrderByDescending(p => p.Value))
                {
                    if (!signals.TryGetValue(item.Key, out var signal)) continue;
                    signal.Controller.UpdateBlocks();
                    bool shunting = route.Draft.Shunting || SignalIntegration.IsShuntingHead(signal);
                    bool allowed = route.State != "releasing" && !route.Faulted && failure.Length == 0 &&
                        !route.PassedSignals.Contains(item.Key.ToString()) && (!shunting || IsShuntingAllowed(signal));
                    aspects[item.Key] = manualShunting.Contains(item.Key) && failure.Length == 0 ? SignalIntegration.ShuntingAspect(signal) :
                        allowed ? (shunting ? SignalIntegration.ShuntingAspect(signal) : SignalIntegration.Evaluate(signal, false)) : SignalIntegration.StopAspect(signal);
                    SignalIntegration.Apply(signal, aspects[item.Key]);
                }
            foreach (var signal in signals.Values)
            {
                if (signal.Parent != null || signal.Controller.Type == SignalType.Distant || signal.Controller.Type == SignalType.Repeater)
                {
                    // These are advance warnings, rather than route endpoints; let them indicate the protected home signal.
                    var home = signal.Parent ?? (signal.Controller as DistantSignalController)?.Home.GetControllerSignal();
                    if (home != null && aspects.TryGetValue(home.Id, out var homeAspect) && homeAspect >= 0 && !home.AllAspects[homeAspect].DisallowPassing)
                        aspects[signal.Id] = SignalIntegration.Evaluate(signal, false);
                }
                SignalIntegration.Apply(signal, aspects[signal.Id]);
            }
            if (!previousShunting.SetEquals(signals.Values.Where(IsShuntingAllowed).Select(s => s.Id)) ||
                previous.Count != aspects.Count || aspects.Any(p => !previous.TryGetValue(p.Key, out var old) || old != p.Value))
            {
                aspectsChanged = true;
                Sessions.AddTag("signalStates");
            }
        }

        public static bool MayReserve(DvSignal signal)
        {
            if (!Controlling || signal.Block == null) return true;
            var blockTracks = new HashSet<string>(signal.Block.AllTracks.Select(TrackId));
            if (DispatchNetwork.Authority) return !routes.Any(r => r.Draft.Ranges.Keys.Any(blockTracks.Contains));
            return !((JArray?)remoteState["routes"] ?? new JArray()).Any(r =>
                ((JArray?)r["tracks"] ?? new JArray()).Any(t => blockTracks.Contains((string)t!)));
        }
        public static int GetAspect(DvSignal signal)
        {
            var values = DispatchNetwork.Authority ? aspects : remoteAspects;
            return failure.Length == 0 && !remoteMismatch && values.TryGetValue(signal.Id, out var aspect) &&
                aspect < signal.AllAspects.Length && !SignalIntegration.IsSubstituteAspect(signal, aspect) ? aspect : SignalIntegration.StopAspect(signal);
        }
        private static JObject DraftJson(Draft draft) => new JObject {
            ["id"] = draft.Id, ["start"] = draft.Start.Id, ["end"] = draft.End.Id,
            ["startName"] = draft.Start.Name, ["endName"] = draft.End.Name,
            ["shunting"] = draft.Shunting, ["length"] = Math.Round(draft.Path.Length),
            ["tracks"] = new JArray(draft.Path.Nodes.Select(id => graph[id].Track)),
            ["switches"] = JObject.FromObject(draft.Path.Switches),
            ["signals"] = new JArray(draft.Signals.Keys),
            ["releasePoint"] = Math.Round(draft.ReleasePoint, 2),
            ["expires"] = draft.Expires.ToString("O")
        };
        private static JObject RouteJson(ActiveRoute route)
        {
            var json = DraftJson(route.Draft);
            json["state"] = route.State; json["dispatcher"] = route.User;
            json["entered"] = route.Release.Entered;
            json["releaseAt"] = route.ReleaseAt?.ToString("O");
            json["cars"] = new JArray(route.Cars);
            return json;
        }
        public static JObject GetState() => !DispatchNetwork.Authority && remoteControl ? (JObject)remoteState.DeepClone() : new JObject {
            ["available"] = running && signals.Count > 0, ["authority"] = DispatchNetwork.Authority,
            ["error"] = failure.Length > 0 ? failure : DispatchNetwork.AdapterFailed ? "Multiplayer adapter unavailable." : "",
            ["routes"] = new JArray(routes.Select(RouteJson)), ["locks"] = JObject.FromObject(locks),
            ["occupiedTracks"] = new JArray(occupiedTracks.OrderBy(id => id, StringComparer.Ordinal)),
            ["manualShunting"] = new JArray(manualShunting.OrderBy(id => id)),
            ["auxiliaryReleaseSeconds"] = Main.settings.auxiliaryReleaseSeconds
        };
        private static JObject SignalState(DvSignal signal)
        {
            int index = GetAspect(signal);
            var aspect = index >= 0 ? signal.AllAspects[index] : null;
            string colour = SignalIntegration.AspectColour(signal, index);
            return new JObject {
                ["id"] = signal.Id, ["aspect"] = aspect?.Id ?? "off",
                ["colour"] = IsShuntingAllowed(signal) ? "white" : colour, ["stop"] = !IsShuntingAllowed(signal) && (aspect == null || index == SignalIntegration.StopAspect(signal) || aspect.DisallowPassing),
                ["shuntingAllowed"] = IsShuntingAllowed(signal),
                ["manualShunting"] = DispatchNetwork.Authority ? manualShunting.Contains(signal.Id) :
                    ((JArray?)remoteState["manualShunting"])?.Any(v => (int)v == signal.Id) == true,
                ["route"] = routes.FirstOrDefault(r => r.Draft.Signals.ContainsKey(signal.Id))?.Draft.Id
            };
        }
        public static JArray GetSignalStates() => new JArray(signals.Values.Select(SignalState));
        public static JArray GetSignals() => new JArray(signals.Values.Select(signal => {
            var json = SignalState(signal);
            var placement = signal.Controller.PlacementInfo;
            var p = signal.Definition.transform.position - WorldMover.currentMove;
            json["name"] = signal.Name;
            json["position"] = new World.Position(p).ToLatLon().ToJson();
            json["rotation"] = SignalRotation(signal);
            json["normal"] = SignalIntegration.CanSelect(signal, false);
            json["shunting"] = SignalIntegration.IsShuntingHead(signal);
            json["canShunt"] = SignalIntegration.CanManualShunt(signal);
            json["track"] = placement.HasValue ? TrackId(placement.Value.Track) : "";
            json["direction"] = placement.HasValue ? TravelDirection(placement.Value).ToString() : "";
            return json;
        }));
        private static void Publish()
        {
            string state = GetState().ToString(Formatting.None);
            if (state != lastState) { lastState = state; Sessions.AddTag("routes"); }
            if (aspectsChanged) Sessions.AddTag("signalStates");
            aspectsChanged = false;
            StateChanged?.Invoke();
            DispatchNetwork.SendState?.Invoke(NetworkSnapshot());
        }
        public static string NetworkSnapshot() => new JObject {
            ["protocol"] = 3, ["layout"] = layout,
            ["shunting"] = new JArray(signals.Values.Where(IsShuntingAllowed).Select(s => s.Id)),
            ["epoch"] = epoch, ["sequence"] = ++sequence, ["enabled"] = Controlling,
            ["state"] = GetState(), ["aspects"] = JObject.FromObject(aspects), ["locks"] = JObject.FromObject(locks)
        }.ToString(Formatting.None);
        public static void ReceiveSnapshot(string json)
        {
            if (DispatchNetwork.Authority) return;
            var snapshot = JObject.Parse(json);
            if (signals.Count == 0 && (bool?)snapshot["enabled"] == true)
            {
                pendingSnapshot = json;
                return;
            }
            string incomingEpoch = (string?)snapshot["epoch"] ?? "";
            long incomingSequence = (long?)snapshot["sequence"] ?? -1;
            if (incomingEpoch == remoteEpoch && incomingSequence <= remoteSequence) return;
            var newLocks = snapshot["locks"]!.ToObject<Dictionary<int, byte>>()!;
            var newAspects = snapshot["aspects"]!.ToObject<Dictionary<int, int>>()!;
            var newShunting = new HashSet<int>(snapshot["shunting"]?.ToObject<int[]>() ?? Array.Empty<int>());
            remoteEpoch = incomingEpoch; remoteSequence = incomingSequence;
            remoteControl = (bool?)snapshot["enabled"] ?? false;
            bool signalsChanged = remoteState["manualShunting"]?.ToString(Formatting.None) != snapshot["state"]?["manualShunting"]?.ToString(Formatting.None) ||
                !newShunting.SetEquals(remoteShunting) || newAspects.Count != remoteAspects.Count || newAspects.Any(p => !remoteAspects.TryGetValue(p.Key, out var old) || old != p.Value);
            string previousState = remoteState.ToString(Formatting.None);
            bool wasMismatch = remoteMismatch;
            remoteMismatch = remoteControl && ((int?)snapshot["protocol"] != 3 || (string?)snapshot["layout"] != layout);
            remoteLocks.Clear(); foreach (var pair in newLocks) remoteLocks[pair.Key] = pair.Value;
            remoteAspects.Clear(); foreach (var pair in newAspects) remoteAspects[pair.Key] = pair.Value;
            remoteShunting.Clear(); remoteShunting.UnionWith(newShunting);
            remoteState = (JObject)snapshot["state"]!; remoteState["authority"] = false;
            if (remoteMismatch)
            {
                remoteState["error"] = "Host/client track or signal layouts differ. Match track mods, signal packs and Remote Dispatch versions. Signals and switches are held locked.";
                foreach (var signal in signals.Values) SignalIntegration.Apply(signal, SignalIntegration.StopAspect(signal));
                if (signalsChanged || !wasMismatch) Sessions.AddTag("signalStates");
                if (previousState != remoteState.ToString(Formatting.None)) Sessions.AddTag("routes");
                return;
            }
            // This also corrects a switch changed locally before its lock snapshot arrived.
            applyingSwitches = true;
            try
            {
                foreach (var pair in remoteLocks)
                    if (pair.Key >= 0 && pair.Key < RailTrackRegistry.Instance.OrderedJunctions.Length)
                    {
                        var junction = RailTrackRegistry.Instance.OrderedJunctions[pair.Key];
                        if (junction.selectedBranch != pair.Value) junction.Switch(Junction.SwitchMode.NO_SOUND, pair.Value);
                    }
                foreach (var signal in signals.Values) SignalIntegration.Apply(signal, GetAspect(signal));
            }
            finally { applyingSwitches = false; }
            if (signalsChanged || wasMismatch) Sessions.AddTag("signalStates");
            if (previousState != remoteState.ToString(Formatting.None)) Sessions.AddTag("routes");
        }
    }
}
