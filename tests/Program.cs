using System;
using System.Collections.Generic;
using System.Linq;
using DvMod.RemoteDispatch;
using Newtonsoft.Json.Linq;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;
using DvSignal = Signals.Game.Signal;

static class Tests
{
    static int assertions;
    static DateTime now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    static RailTrack a = null!, b = null!, c = null!, d = null!;
    static Junction split = null!, merge = null!;
    static TrainCar engine = null!, wagon = null!;
    static DvSignal start = null!, end = null!;
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    static void Reject(Action action, string message) { try { action(); } catch (DispatchException) { assertions++; return; } throw new Exception(message); }
    static void Tick(double seconds = 0.11) { now = now.AddSeconds(seconds); Time.unscaledTime += (float)seconds; RouteManager.Tick(); }
    static void Move(TrainCar car, RailTrack track, params double[] spans)
    {
        for (int i = 0; i < car.Bogies.Length; i++)
        {
            car.Bogies[i].track = track; car.Bogies[i].traveller!.Span = spans[i];
            float offset = track == a ? 0 : track == c ? 200 : 100;
            car.Bogies[i].transform.position = new Vector3(offset + (float)spans[i], 0, 0);
        }
    }
    static DvSignal Signal(int id, RailTrack track, double span, bool shunting)
    {
        var controller = new TrackSignalController { PlacementInfo = new SignalPlacementInfo { Track = track, Span = span, Direction = TrackDirection.In } };
        var signal = new DvSignal { Id = id, Name = "S-" + id, Controller = controller, IsShunting = shunting };
        if (shunting) controller.ShuntingSignals = new[] { signal }; else controller.Signals = new[] { signal };
        SignalManager.Instance.AllControllers.Add(controller);
        return signal;
    }
    static void World(bool shunting = false)
    {
        RouteManager.Stop();
        RouteManager.Clock = () => now;
        SignalIntegration.Available = true; DispatchNetwork.Connected = false; DispatchNetwork.Host = true; DispatchNetwork.AdapterFailed = false;
        SignalManager.Instance.AllControllers.Clear(); TrainCarRegistry.Instance.logicCarToTrainCar.Clear();
        TrackChecker.Occupied.Clear(); TrackChecker.s_intersectionMap.Clear(); TrackReserver.Reserved.Clear();
        a = new RailTrack { Id = "A" }; b = new RailTrack { Id = "B" }; c = new RailTrack { Id = "C" }; d = new RailTrack { Id = "D", Length = 120 };
        split = new Junction { inBranch = new Junction.Branch { track = a, first = false }, position = new Vector3(100, 0, 0) };
        split.outBranches.Add(new Junction.Branch { track = b, first = true }); split.outBranches.Add(new Junction.Branch { track = d, first = true });
        merge = new Junction { inBranch = new Junction.Branch { track = c, first = true }, position = new Vector3(200, 0, 0) };
        merge.outBranches.Add(new Junction.Branch { track = b, first = false }); merge.outBranches.Add(new Junction.Branch { track = d, first = false });
        a.outJunction = split; b.inJunction = d.inJunction = split; b.outJunction = d.outJunction = merge; c.inJunction = merge;
        RailTrackRegistry.RailTracks = new[] { a, b, c, d }; RailTrackRegistry.Instance.OrderedJunctions = new[] { split, merge };
        start = Signal(1, a, 20, shunting); end = Signal(2, c, 50, shunting);
        engine = new TrainCar { CarGUID = "engine", Bogies = new[] { new Bogie(), new Bogie() } };
        wagon = new TrainCar { CarGUID = "wagon", Bogies = new[] { new Bogie(), new Bogie() } };
        var set = new Trainset(); set.cars.AddRange(new[] { engine, wagon }); engine.trainset = wagon.trainset = set;
        TrainCarRegistry.Instance.logicCarToTrainCar.Add("engine", engine); TrainCarRegistry.Instance.logicCarToTrainCar.Add("wagon", wagon);
        Move(engine, a, 10, 5); Move(wagon, a, 1, 0);
        RouteManager.Start(); Tick();
    }
    static JArray Routes() => (JArray)RouteManager.GetState()["routes"]!;
    static string Set(int choice = 0)
    {
        var preview = RouteManager.Preview(1, 2, start.IsShunting);
        var candidate = preview["candidates"]![choice]!;
        return (string)RouteManager.Establish((string)candidate["id"]!, "dispatcher")["id"]!;
    }
    static void GraphCases()
    {
        var graph = new Dictionary<string, RouteNode>();
        foreach (var id in new[] { "A", "B", "C", "D" }) graph[id] = new RouteNode { Id = id, Track = id, Length = 100 };
        graph["A"].Edges.Add(new RouteEdge { Target = "B", Junction = 0, Branch = 0 });
        graph["A"].Edges.Add(new RouteEdge { Target = "D", Junction = 0, Branch = 1 });
        graph["B"].Edges.Add(new RouteEdge { Target = "C" }); graph["D"].Edges.Add(new RouteEdge { Target = "C" });
        var paths = RoutePlanning.FindPaths(graph, "A", "C", 20, 50, out var truncated);
        Check(paths.Count == 2 && !truncated, "Return both manual choices.");
        Check(paths.All(p => p.Length == 230), "Use partial endpoint tracks in route length.");
        Check(paths.Select(p => p.Switches[0]).Distinct().Count() == 2, "Preserve different branch requirements.");
        Check(RoutePlanning.FindPaths(graph, "C", "A", 0, 0, out _).Count == 0, "Reject unreachable reverse traversal.");
        Check(RoutePlanning.FindPaths(graph, "A", "A", 50, 20, out _).Count == 0, "Reject backward endpoints on same track.");
        Check(RoutePlanning.FindPaths(graph, "A", "A", 20, 50, out _).Single().Length == 30, "Support two forward signals on the same track.");
        graph["B"].Edges.Clear(); graph["B"].Edges.Add(new RouteEdge { Target = "C", Junction = 0, Branch = 1 });
        Check(RoutePlanning.FindPaths(graph, "A", "C", 20, 50, out _).Count == 1, "Reject contradictory settings for the same switch.");
        graph["D"].Edges.Add(new RouteEdge { Target = "A" });
        Check(RoutePlanning.FindPaths(graph, "A", "C", 20, 50, out _).Count == 1, "Bound cyclic topology.");
    }
    static void ManagerCases()
    {
        World();
        Check(start.CurrentAspectIndex == 0 && end.CurrentAspectIndex == 0, "Hold unreserved signals at stop.");
        start.Definition.transform.eulerAngles = new Vector3(0, 180, 0);
        Check(RouteManager.SignalRotation(start) == 0, "Use train travel direction from track geometry rather than the prefab yaw.");
        a.Forward = new Vector3(1, 0, 0);
        Check(RouteManager.SignalRotation(start) == 90, "Draw eastward train travel correctly.");
        a.Forward = new Vector3(-1, 0, 0);
        Check(RouteManager.SignalRotation(start) == 270, "Draw westward train travel correctly.");
        a.Forward = new Vector3(0, 0, 1);
        Check((string)RouteManager.GetSignals()[0]!["direction"]! == "Out", "Placement facing In authorizes Out travel.");
        Check(RouteManager.GetSignalStates()[0]!["position"] == null, "Aspect updates omit static signal geometry.");
        var preview = RouteManager.Preview(1, 2, false);
        Check(((JArray)preview["candidates"]!).Count == 2, "Enumerate both physical routes.");
        Check(split.selectedBranch == 0 && Routes().Count == 0, "Preview must not switch or reserve anything.");
        string id = Set(1);
        Check(split.selectedBranch == 1 && merge.selectedBranch == 1, "Only the manually chosen path is aligned.");
        Check(start.CurrentAspectIndex == 1 && end.CurrentAspectIndex == 0, "Clear entrance while retaining destination stop.");
        Check(!RouteManager.AllowSwitch(split, 0) && RouteManager.AllowSwitch(split, 1), "Lock changes, allow idempotent branch requests.");
        split.Switch(Junction.SwitchMode.FORCED, 0);
        Check(split.selectedBranch == 1, "Forced switching also obeys route locks.");
        Reject(() => Set(), "Reject overlapping simultaneous route.");
        RouteManager.Cancel(id, false, "dispatcher"); Check(Routes().Count == 0 && !RouteManager.HasLocks, "Cancel an unused route.");

        World();
        start.Controller.PlacementInfo = new SignalPlacementInfo { Track = c, Span = 50, Direction = TrackDirection.Out };
        end.Controller.PlacementInfo = new SignalPlacementInfo { Track = a, Span = 20, Direction = TrackDirection.Out };
        Move(engine, c, 60, 55); Move(wagon, c, 70, 65); Tick();
        Check(RouteManager.SignalRotation(start) == 180, "A signal facing Out points toward In travel on the map.");
        Check(((JArray)RouteManager.Preview(1, 2, false)["candidates"]!).Count == 2, "Route reverse travel across merging and splitting junctions.");
        id = Set(1);
        Check(split.selectedBranch == 1 && merge.selectedBranch == 1, "Align the manually chosen reverse path.");
        Move(engine, c, 60, 55); Move(wagon, c, 70, 65); Tick();
        Move(engine, c, 45, 40); Tick();
        Check((bool)Routes()[0]!["entered"]! && start.CurrentAspectIndex == 0, "Detect train entry in decreasing track span direction.");
        Move(engine, a, 40, 35); Move(wagon, a, 40, 35); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks, "Release reverse travel after the last switch, before the destination signal.");

        World(); Move(wagon, b, 50, 45); Tick();
        var occupied = RouteManager.Preview(1, 2, false);
        Check(occupied["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "Identify occupied normal path.");
        Reject(() => RouteManager.Establish((string)occupied["candidates"]![0]!["id"]!, "dispatcher"), "Recheck occupancy at commit.");
        Check((string?)occupied["candidates"]![1]!["unavailable"] == null, "Offer the unoccupied alternate path.");
        World(true); Move(wagon, b, 50, 45); Tick();
        id = Set(); Check(Routes().Count == 1 && (bool)Routes()[0]!["shunting"]!, "Allow occupied Rangierfahrstraße.");
        Reject(() => RouteManager.Cancel(id, false, "dispatcher"), "Do not cancel a route with wagons in it.");
        RouteManager.Cancel(id, true, "dispatcher");
        Check(start.CurrentAspectIndex == 0 && RouteManager.HasLocks, "Hilfsauflösung stops signals but retains locks.");
        Tick(89); Check(RouteManager.HasLocks, "Keep auxiliary locks for the full delay.");
        Tick(2); Check(!RouteManager.HasLocks, "Auxiliary release unlocks after countdown.");

        World(); id = Set(); Move(engine, a, 25, 21); Tick();
        Check((bool)Routes()[0]!["entered"]! && start.CurrentAspectIndex == 0, "Bind entering consist and replace entrance to stop.");
        Reject(() => RouteManager.Cancel(id, false, "dispatcher"), "Entered route requires Hilfsauflösung.");
        Move(engine, c, 70, 65); Move(wagon, b, 80, 75); Tick(3);
        Check(RouteManager.HasLocks, "Locomotive passing cannot release before the last wagon.");
        engine.trainset!.cars.Remove(wagon); wagon.trainset = new Trainset(); wagon.trainset.cars.Add(wagon);
        Tick(3); Check(RouteManager.HasLocks, "Detached wagon keeps route locked.");
        Move(wagon, c, 12, 11); Tick(3); Check(RouteManager.HasLocks, "Rear clearance includes the wagon overhang margin at the last switch.");
        Move(wagon, c, 30, 25); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks && Routes().Count == 0, "Release before the destination when every captured wagon clears the last switch.");
        Check(((JArray)RouteManager.GetState()["occupiedTracks"]!).Any(v => (string?)v == "C"), "Retain occupied target track indication after switch release.");
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "An occupied destination track blocks a new normal route after release.");
        Move(wagon, c, 90, 85); Tick();
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "Even occupancy beyond the destination signal blocks the whole target track.");
        TrainCarRegistry.Instance.logicCarToTrainCar.Clear(); Tick(.4);
        var vacantPreview = RouteManager.Preview(1, 2, false);
        Check((string?)vacantPreview["candidates"]![0]!["unavailable"] == null, "A normal route becomes available when the target track is vacant: " + vacantPreview["candidates"]![0]!["unavailable"]);

        World();
        var beyond = new RailTrack { Id = "E" };
        c.outBranch = new Junction.Branch { track = beyond, first = true };
        beyond.inBranch = new Junction.Branch { track = c, first = false };
        RailTrackRegistry.RailTracks = RailTrackRegistry.RailTracks.Concat(new[] { beyond }).ToArray();
        Signal(99, beyond, 50, false); Tick();
        Move(engine, beyond, 5, 2); Move(wagon, beyond, 6, 3); Tick(.4);
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "Rear body overhang keeps a target track occupied after its last bogie exits.");
        Move(engine, beyond, 30, 25); Move(wagon, beyond, 30, 25); Tick(.4);
        Check((string?)RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"] == null, "Clear the target track only after the rear body clears its boundary.");

        World(); id = Set(); Move(engine, a, 25, 21); Tick();
        Move(engine, c, 70, 65); Move(wagon, c, 70, 65); Tick();
        Move(wagon, b, 80, 75); Tick(3); Check(RouteManager.HasLocks, "Reversal cancels automatic release.");
        TrainCarRegistry.Instance.logicCarToTrainCar.Remove("wagon"); Tick(3);
        Check(RouteManager.HasLocks, "Missing cars do not count as passing the trigger.");
        TrainCarRegistry.Instance.logicCarToTrainCar.Add("wagon", wagon);
        Move(wagon, c, 70, 65); Tick(); Tick(3);
        Check(RouteManager.HasLocks, "Restoring a missing wagon does not silently clear the route fault.");
        RouteManager.Cancel(id, true, "dispatcher"); Tick(91); Check(!RouteManager.HasLocks, "Hilfsauflösung recovers a route with a missing wagon.");

        World(); id = Set(); SignalIntegration.Available = false;
        RouteManager.Cancel(id, true, "dispatcher"); Tick(89);
        Check(RouteManager.HasLocks, "Unavailable signal mod does not shorten auxiliary release delay.");
        Tick(2); Check(!RouteManager.HasLocks, "Hilfsauflösung remains usable when the signal mod is unavailable.");

        World(); TrackReserver.Reserved.Add(b);
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "Respect existing DV Signals reservations.");
        World(); var expired = RouteManager.Preview(1, 2, false); now = now.AddMinutes(3);
        Reject(() => RouteManager.Establish((string)expired["candidates"]![0]!["id"]!, "dispatcher"), "Reject expired previews.");
        World(); id = Set(); split.selectedBranch = 1; Tick();
        Check((string)Routes()[0]!["state"]! == "fault" && start.CurrentAspectIndex == 0, "Detect direct field mutation and hold signals at stop.");
        RouteManager.Cancel(id, true, "dispatcher"); Tick(91); Check(!RouteManager.HasLocks, "Recover a switch fault with Hilfsauflösung.");

        World(); TrackChecker.s_intersectionMap[a] = new TrackChecker.TrackIntersectionPoints { Busy = true };
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "An approaching train must not hide an occupied crossing.");
        World(); TrackChecker.Occupied.Add(a);
        Check(RouteManager.Preview(1, 2, false)["candidates"]![0]!["unavailable"]!.Type == JTokenType.String, "Respect manual occupancy even when an approaching train is on that track.");
        World(); id = Set(); Move(engine, a, 25, 21); Tick();
        wagon.Bounds = new Bounds { min = new Vector3(0, 0, -20), max = new Vector3(0, 0, 20) };
        Move(engine, c, 90, 85); Move(wagon, c, 25, 24); Tick(3);
        Check(RouteManager.HasLocks, "Account for custom wagon overhang beyond the default clearance.");
        Move(wagon, c, 40, 35); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks, "Release when the custom wagon body clears the trigger.");
        World(); var forwardHead = Signal(99, c, 40, false); Tick(); Set();
        Move(engine, a, 25, 21); Tick(); Move(engine, c, 30, 25); Move(wagon, c, 30, 25); Tick();
        for (int i = 0; i < 25 && RouteManager.HasLocks; i++) Tick();
        Check(!RouteManager.HasLocks && forwardHead.CurrentAspectIndex == 0, "Immediately stop unpassed signals beyond the last switch when the route releases.");
        World(); end.Controller.PlacementInfo = new SignalPlacementInfo { Track = a, Span = 80, Direction = TrackDirection.In }; Set();
        Move(engine, a, 25, 21); Tick(); Move(engine, a, 50, 45); Move(wagon, a, 35, 30); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks, "A route with no switches releases after the whole train clears its entrance, without passing the destination.");
        World(); Set(); Move(engine, a, 25, 21); Tick(); Move(engine, c, 30, 25); Move(wagon, c, 30, 25); Tick();
        var furtherTrack = new RailTrack { Id = "further" };
        Move(engine, furtherTrack, 80, 75); Move(wagon, furtherTrack, 80, 75); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks, "Preserve confirmed switch clearance if the train subsequently leaves the observed route tracks.");
    }
    static void ShuntingCases()
    {
        var mislabelled = new[] {
            new SignalAspectInfo { Id = "Ms2", DisallowPassing = true, ShuntingPermission = true },
            new SignalAspectInfo { Id = "Ms1", DisallowPassing = false, StopLamp = true }
        };
        Check(SignalAspectRules.SelectStop(mislabelled, false) == 1, "Incorrect pack DisallowPassing metadata cannot select white Ms2 as stop.");
        Check(SignalAspectRules.SelectShunting(mislabelled, 1) == 0, "Explicit shunting selects Ms2 despite incorrect DisallowPassing metadata.");
        Check(SignalAspectRules.SelectStop(new[] { new SignalAspectInfo { ShuntingPermission = true } }, false) == -1, "Turn off a head with no restrictive aspect instead of allowing unintended shunting.");
        Check(SignalAspectRules.SelectStop(new[] { new SignalAspectInfo { Id = "warning" } }, true) == 0, "Distant signals retain their restrictive warning fallback.");
        World();
        Check(!start.ShuntingAllowed && !end.ShuntingAllowed, "Override the native default shunting permission at startup.");
        Move(wagon, b, 50, 45); split.selectedBranch = 1; merge.selectedBranch = 1; Tick();
        RouteManager.SetManualShunting(1, true, "dispatcher");
        Check(start.ShuntingAllowed && start.CurrentAspectIndex == 0, "Combined main signal shows shunting permission while its main aspect stays at stop.");
        Check(Routes().Count == 0 && !RouteManager.HasLocks && split.selectedBranch == 1 && merge.selectedBranch == 1, "Manual shunting neither searches paths nor checks, aligns or locks switches.");
        Check((bool)RouteManager.GetSignalStates()[0]!["shuntingAllowed"]! && !(bool)RouteManager.GetSignalStates()[0]!["stop"]!, "Expose combined shunting permission separately from the red main aspect.");
        Tick(10); Check(start.ShuntingAllowed, "Manual permission persists until explicitly revoked.");
        RouteManager.SetManualShunting(1, false, "dispatcher");
        Check(!start.ShuntingAllowed && start.CurrentAspectIndex == 0, "Rangierhalt revokes combined signal permission.");
        World(true); RouteManager.SetManualShunting(1, true, "dispatcher");
        Check(start.CurrentAspectIndex == 1 && start.ShuntingAllowed && Routes().Count == 0, "Standalone shunting signal can show permission without a route.");
        RouteManager.SetManualShunting(1, false, "dispatcher");
        Check(start.CurrentAspectIndex == 0 && !start.ShuntingAllowed, "Standalone shunting signal returns to stop.");
        var routeId = Set(); RouteManager.SetManualShunting(1, false, "dispatcher");
        Check(!start.ShuntingAllowed && start.CurrentAspectIndex == 0 && RouteManager.HasLocks, "Rangierhalt stops a formal shunting route without releasing its switches.");
        RouteManager.Cancel(routeId, false, "dispatcher");
        Move(wagon, c, 80, 75); Tick(); Set();
        Check(RouteManager.HasLocks && start.ShuntingAllowed, "A new formal Rangierfahrstraße can enter occupied target track and authorize its signal.");
        World(); var auxiliaryRoute = Set(); RouteManager.SetManualShunting(1, true, "dispatcher");
        RouteManager.Cancel(auxiliaryRoute, true, "dispatcher");
        Check(!start.ShuntingAllowed && ((JArray)RouteManager.GetState()["manualShunting"]!).Count == 0 && RouteManager.HasLocks,
            "Hilfsauflösung revokes manual permission on route signals and retains the switch locks.");
        World(); var intermediate = Signal(3, b, 50, false); Tick();
        var mainShunting = RouteManager.Preview(1, 2, true);
        RouteManager.Establish((string)mainShunting["candidates"]![0]!["id"]!, "dispatcher");
        Check(start.CurrentAspectIndex == 0 && start.ShuntingAllowed && intermediate.ShuntingAllowed, "Formal Rangierfahrstraße authorizes combined main signals without a green main aspect.");
        Move(engine, a, 25, 21); Tick(); Move(engine, b, 40, 35); Tick();
        var messages = new List<string>(); DispatchNetwork.SendState = messages.Add;
        Move(engine, b, 60, 55); Tick(); DispatchNetwork.SendState = null;
        Check(!intermediate.ShuntingAllowed && messages.Any(m => !((JArray)JObject.Parse(m)["shunting"]!).Any(id => (int)id == 3)),
            "Immediately replicate combined shunting indicator returning to stop even when its main aspect index stays unchanged.");
        World(); RouteManager.SetManualShunting(1, true, "dispatcher");
        string snapshot = RouteManager.NetworkSnapshot();
        World(); DispatchNetwork.Connected = true; DispatchNetwork.Host = false; Tick(); RouteManager.ReceiveSnapshot(snapshot);
        Check(start.ShuntingAllowed && start.CurrentAspectIndex == 0 && !RouteManager.HasLocks, "Multiplayer replicates manual shunting independently of route aspects and locks.");
        Reject(() => RouteManager.SetManualShunting(1, true, "client"), "Client consoles cannot grant their own shunting permission.");
    }
    static void NetworkCases()
    {
        World(); Set(1); string snapshot = RouteManager.NetworkSnapshot();
        World(); DispatchNetwork.Connected = true; DispatchNetwork.Host = false; Tick();
        RouteManager.ReceiveSnapshot(snapshot);
        Check(split.selectedBranch == 1 && RouteManager.TryGetLock(split, out var branch) && branch == 1, "Replicate host lock and branch state.");
        Check(start.CurrentAspectIndex == 1 && end.CurrentAspectIndex == 0, "Replicate host signal aspects.");
        Check(!(bool)RouteManager.GetState()["authority"]!, "Clients cannot become route authority.");
        Sessions.Tags.Clear();
        var repeat = JObject.Parse(snapshot); repeat["sequence"] = (long)repeat["sequence"]! + 1;
        RouteManager.ReceiveSnapshot(repeat.ToString());
        Check(Sessions.Tags.Count == 0, "Periodic identical multiplayer snapshots do not reload map data.");
        Reject(() => RouteManager.Preview(1, 2, false), "Reject client route creation.");
        var stale = JObject.Parse(snapshot); stale["sequence"] = 0; stale["locks"] = new JObject { ["0"] = 0 };
        RouteManager.ReceiveSnapshot(stale.ToString());
        Check(RouteManager.TryGetLock(split, out branch) && branch == 1, "Ignore stale lock snapshots.");
        var release = JObject.Parse(snapshot); release["sequence"] = 999; release["locks"] = new JObject(); release["aspects"] = new JObject { ["1"] = 0, ["2"] = 0 }; release["state"]!["routes"] = new JArray();
        RouteManager.ReceiveSnapshot(release.ToString());
        Check(!RouteManager.HasLocks && start.CurrentAspectIndex == 0, "Replicate release and replace signals to stop.");
        RouteManager.ResetRemote(); Check(!RouteManager.HasLocks, "Clear client session state on disconnect.");
        var mismatch = JObject.Parse(snapshot); mismatch["epoch"] = "different"; mismatch["layout"] = "different";
        RouteManager.ReceiveSnapshot(mismatch.ToString());
        Check(start.CurrentAspectIndex == 0 && RouteManager.TryGetLock(split, out _), "Hold signals and switches on incompatible multiplayer layouts.");
        Check(((string)RouteManager.GetState()["error"]!).Contains("layouts differ"), "Explain multiplayer layout mismatch.");
    }
    static void ThroughShuntingCases()
    {
        World();
        var tm = Signal(3, b, 50, true);
        var opposite = Signal(4, b, 55, true);
        opposite.Controller.PlacementInfo = new SignalPlacementInfo { Track = b, Span = 55, Direction = TrackDirection.Out };
        var alternate = Signal(5, d, 50, true);
        var beyondDestination = Signal(6, c, 80, true);
        var atDestination = Signal(7, c, 50, true);
        Tick();
        var preview = RouteManager.Preview(1, 2, false);
        Check(((JArray)preview["candidates"]![0]!["signals"]!).Any(v => (int)v == 3), "Include an intermediate Tm in a normal Fahrstraße.");
        string id = RouteManager.Establish((string)preview["candidates"]![0]!["id"]!, "dispatcher")["id"]!.Value<string>()!;
        Check(start.CurrentAspectIndex == 1 && !start.ShuntingAllowed && tm.CurrentAspectIndex == 1 && tm.ShuntingAllowed,
            "Normal route keeps its main entrance aspect and authorizes intermediate Tm white.");
        Check(!opposite.ShuntingAllowed && !alternate.ShuntingAllowed && !beyondDestination.ShuntingAllowed && !atDestination.ShuntingAllowed,
            "Do not authorize opposing heads, other paths or Tm at/beyond the destination.");
        RouteManager.SetManualShunting(3, false, "dispatcher");
        Check(!tm.ShuntingAllowed && tm.CurrentAspectIndex == 0 && RouteManager.HasLocks, "Rangierhalt stops a through Tm without releasing the normal route.");
        RouteManager.SetManualShunting(3, true, "dispatcher");
        RouteManager.Cancel(id, true, "dispatcher");
        Check(!tm.ShuntingAllowed && tm.CurrentAspectIndex == 0 && RouteManager.HasLocks, "Hilfsauflösung stops intermediate Tm immediately.");
        World(); tm = Signal(3, b, 50, true); Tick(); Set();
        string snapshot = RouteManager.NetworkSnapshot();
        World(); tm = Signal(3, b, 50, true); Tick(); DispatchNetwork.Connected = true; DispatchNetwork.Host = false;
        RouteManager.ReceiveSnapshot(snapshot);
        Check(tm.ShuntingAllowed && tm.CurrentAspectIndex == 1, "Replicate a normal route's intermediate Tm permission to clients.");
        World(); tm = Signal(3, b, 50, true); Tick(); Set();
        Move(engine, a, 25, 21); Tick(); Move(engine, b, 40, 35); Tick();
        Check(tm.ShuntingAllowed, "Keep through Tm white until the approaching train reaches it.");
        Move(engine, b, 60, 55); Tick();
        Check(!tm.ShuntingAllowed && tm.CurrentAspectIndex == 0 && RouteManager.HasLocks, "Return through Tm to stop on train passage, while retaining switch locks.");
        World(); tm = Signal(3, b, 50, true); Tick(); id = Set(); RouteManager.Cancel(id, false, "dispatcher");
        Check(!tm.ShuntingAllowed && tm.CurrentAspectIndex == 0, "Cancelling an unused normal route stops its through Tm.");
        World();
        var junctionController = new JunctionSignalController {
            Junction = split, PlacementInfo = new SignalPlacementInfo { Track = a, Span = 70, Direction = TrackDirection.In }
        };
        var main = new DvSignal { Id = 3, Name = "Main", Controller = junctionController };
        var tm0 = new DvSignal { Id = 4, Name = "Tm0", IsShunting = true, Controller = junctionController };
        var tm1 = new DvSignal { Id = 5, Name = "Tm1", IsShunting = true, Controller = junctionController };
        junctionController.Signals = new[] { main }; junctionController.ShuntingSignals = new[] { tm0, tm1 };
        SignalManager.Instance.AllControllers.Add(junctionController); Tick(); id = Set(1);
        Check(main.CurrentAspectIndex == 1 && !main.ShuntingAllowed && !tm0.ShuntingAllowed && tm1.ShuntingAllowed,
            "Select the correct shunting head at a multi-head junction independently of the main-head array.");
        RouteManager.Cancel(id, false, "dispatcher");
        var shunting = RouteManager.Preview(1, 2, true);
        RouteManager.Establish((string)shunting["candidates"]![1]!["id"]!, "dispatcher");
        Check(main.CurrentAspectIndex == 0 && main.ShuntingAllowed && !tm0.ShuntingAllowed && tm1.ShuntingAllowed,
            "A formal shunting route selects main and shunting junction heads from their own arrays.");
        World(); tm = Signal(3, c, 40, true); Tick(); Set();
        Move(engine, a, 25, 21); Tick(); Move(engine, c, 30, 25); Move(wagon, c, 30, 25); Tick(); Tick(2.2);
        Check(!RouteManager.HasLocks && !tm.ShuntingAllowed && tm.CurrentAspectIndex == 0,
            "Automatic switch release stops an unpassed through Tm before the destination.");
    }
    static void SpawnOccupancyCases()
    {
        World(); DispatchNetwork.Connected = true; DispatchNetwork.Host = true;
        var preview = RouteManager.Preview(1, 2, false);
        var spawned = new TrainCar { CarGUID = "generated-wagon", Bogies = new[] { new Bogie(), new Bogie() } };
        Move(spawned, b, 60, 55);
        TrainCarRegistry.Instance.logicCarToTrainCar.Add("generated-wagon", spawned);
        Sessions.Tags.Clear(); Tick(.4);
        Check(((JArray)RouteManager.GetState()["occupiedTracks"]!).Any(v => (string?)v == "B") && Sessions.Tags.Contains("routes"),
            "A wagon generated after dispatch starts publishes new occupancy even without active routes.");
        Reject(() => RouteManager.Establish((string)preview["candidates"]![0]!["id"]!, "dispatcher"),
            "Recheck newly generated wagons when committing a previously clear route preview.");
        string snapshot = RouteManager.NetworkSnapshot();
        World(); DispatchNetwork.Connected = true; DispatchNetwork.Host = false; Tick(); RouteManager.ReceiveSnapshot(snapshot);
        Check(((JArray)RouteManager.GetState()["occupiedTracks"]!).Any(v => (string?)v == "B"),
            "Clients display host occupancy for generated wagons absent from their local registry.");
        World(); TrainCarRegistry.Instance.logicCarToTrainCar.Add("generated-wagon", spawned); Tick(.4);
        Move(spawned, d, 60, 55); Tick(.4);
        var occupied = (JArray)RouteManager.GetState()["occupiedTracks"]!;
        Check(occupied.Any(v => (string?)v == "D") && !occupied.Any(v => (string?)v == "B"),
            "A generated wagon moving between tracks updates occupancy without a route.");
        TrainCarRegistry.Instance.logicCarToTrainCar.Remove("generated-wagon"); Tick(.4);
        Check(!((JArray)RouteManager.GetState()["occupiedTracks"]!).Any(v => (string?)v == "D"),
            "Deleting a generated wagon clears its track indication.");
    }
    public static void Main()
    {
        GraphCases(); ManagerCases(); NetworkCases(); ShuntingCases(); ThroughShuntingCases(); SpawnOccupancyCases();
        Console.WriteLine($"PASS: {assertions} assertions, including route commits, train release and multiplayer snapshots.");
    }
}
