# Remote Dispatch with DV Signals

Adds dispatcher-controlled Fahrstraßen, Rangierfahrstraßen, manual shunting permissions and Gleisfreimeldung to Remote Dispatch.

## Installation

Install the ZIP produced by `package.ps1` with Unity Mod Manager, replacing Remote Dispatch. This build requires UMM 0.31.1 or newer and DV Signals 1.1.3. Keep DV Signals, its bundle, your signal pack (including PLSignals), and DVLangHelper installed. The ZIP contains only Remote Dispatch and its multiplayer adapter, without game DLLs. Restart the game after updating, then reload the browser with Ctrl+F5 to load the new scripts.

Version 1.4.1 fixes the 1.4.0 startup error `You can only patch implemented methods/constructors` from the inherited `DisallowPassing` getter. Pack stop/shunting metadata is corrected directly while dispatch controls the signals, and restored when control ends. Generic getters are no longer patched.

Version 1.4.2 prefers plain `S1` over the Polish `Sz` substitute aspect, even when `Sz` appears first and includes a red lamp. Substitute permissions are excluded from automatic route aspects and indicators. Repeater map colors use both steady and blinking lamps: `Sp1` remains yellow with its identifying white lamp, rather than appearing green on the map.

Version 1.4.3 also clears intermediate shunting heads facing along a normal Fahrstraße, selecting the correct head at multi-head junctions. They return to stop on passage, cancellation, faults or release. Red occupancy has its own canvas above route and preview highlights, so later redraws cannot cover an occupied track with green.

For multiplayer, install the same Remote Dispatch build, DV Signals, track mods and signal pack on **the host and every client**. The adapter targets AMacro's Multiplayer beta with MultiplayerAPI 1.1.0. Multiplayer supplies `MultiplayerAPI.dll`; do not copy another API DLL from a build folder into the game. Dispatchers should use the host's browser console.

## Dispatching

1. Enable both mods. Main and shunting signals are held at their restrictive aspect until explicitly authorized. Native default shunting flags and white indicators are overridden. The PLSignals Ms1/Ms2 choice is corrected even when the pack's DisallowPassing metadata is inverted. Distant signals use their restrictive warning aspect when no red lamp exists. A head without any restrictive aspect is switched off.
2. Open `http://localhost:7245` (or the host's address and configured port). Grant your browser username **Fahrstraßen** permission in the in-game Remote Dispatch settings. Grant **Hilfsauflösung** separately where needed.
3. Open the traffic-light tab and choose Fahrstraße or Rangierfahrstraße. Click the entrance and destination signal **in the direction of travel**, click a candidate path to preview it, then click **Set Fahrstraße**. Two signal clicks alone never move switches. Both main and shunting heads can authorize Rangierfahrstraßen.
4. Normal routes reject occupied tracks, occupied crossings and conflicting reservations. Rangierfahrstraßen permit occupied track for coupling, but cannot move a switch fouled by a vehicle.
5. The chosen path aligns and locks its switches. DV Signals selects the aspect appropriate to the aligned route, upcoming signals and speed conditions. Intermediate standalone shunting heads facing along a normal Fahrstraße show white; opposing heads and heads on other branches remain at stop. The destination stays at stop unless a continuation route authorizes it.
6. Signals return to stop as the entering train passes them. Switches release once **every wagon body clears the last switch** and the switches stay clear for two seconds. The destination signal does not have to be passed. A route with no switches releases after the whole train clears its entrance. Detached wagons retain the lock. Missing or derailed vehicles require Hilfsauflösung. Unpassed route signals return to stop as soon as the route releases.
7. Gleisfreimeldung is independent of route locks. Occupied tracks appear red. Bogies, rear overhang, tracks between a car's bogies, and native manual occupancy contribute to the indication. A normal route into an occupied target track remains blocked after its switches unlock, including when the vehicle is beyond the destination signal. Formal Rangierfahrstraßen may enter occupied track. Vehicles waiting behind the entrance signal do not block their own departure route.

## Manual Rangierfahrt erlaubt

Choose **Rangierfahrt frei (ohne Fahrstraße)** and click a main or shunting signal once. This grants shunting permission without choosing a destination, searching a path, checking occupancy or switch positions, aligning switches, or adding locks. Combined main signals keep their red main indication and show their white shunting indicator. Standalone shunting heads show their permissive shunting aspect.

Permission persists until another click or the **Rangierhalt** button revokes it. It uses the existing Fahrstraßen permission. Existing locks from a formal route remain in place. Setting a formal route takes over its signal permissions; Hilfsauflösung revokes manual shunting on that route's signals. Manual permissions are not saved across reloads or signal-pack changes.

Zoom in to show signals (zoom level 16 and above). Main and shunting markers are offset so overlapping heads can be selected separately. Arrows show the train's direction of travel, derived from the track geometry rather than the signal model. Hover a marker for its name and aspect. Active routes are green, shunting routes purple, and the preview dashed yellow.

Signal arrows use the corrected travel direction: DV Signals placement direction points towards the approaching train. Signal markers are created only near the visible map area; aspect changes update existing markers, while unchanged routes and switches keep their overlays. Initial map data loads once, and subsequent aspect updates omit signal geometry. Identical multiplayer snapshots do not trigger map redraws. Multiplayer peers must all update to **1.4.3**, which uses snapshot protocol **3** for shunting permissions and occupancy.

## Cancellation and Hilfsauflösung

Unused, unoccupied routes can be cancelled immediately. After train entry, ordinary cancellation is rejected.

Hilfsauflösung returns route signals to stop immediately and holds switch locks for **90 seconds** by default. It then releases the route even if vehicles remain. The dispatcher must explicitly confirm this and have Hilfsauflösung permission. The delay is configurable in the game mod settings. Requests and releases are logged with the dispatcher name.

## Multiplayer

The host establishes routes, detects train passage and measures track occupancy. Reliable snapshots synchronize routes, signal aspects, white shunting indicators, manual permissions, occupancy and locks to clients, including late joiners. Indicator changes are published even when a combined main signal's aspect remains red. Both Junction.Switch overloads are intercepted, including forced switching. Incoming multiplayer switch packets are corrected before broadcast. Mismatched track or signal layouts hold client signals at stop and block switch changes.

Client browser consoles display host state but reject route commands. Use the host console to dispatch. DV Signals reservations cannot overlap established routes. Remote Dispatch cannot be disabled while a route is active.

The targeted multiplayer beta checks all players' distance for station job/wagon generation and handles client generation requests on the host. Remote Dispatch polls the host's complete registered-car collection, including newly generated or distant wagons, and publishes their occupancy to clients even if those clients have not loaded the wagons locally. In single player, wagons that the scenario has not generated yet cannot occupy a track; Remote Dispatch does not force wagon generation. Once generated, their track indication updates within the next occupancy poll (normally 0.25 seconds). Actual two-player generation and transport still require an in-game session to verify.

## Limits and validation

- Routes and locks are **not saved across game reloads**. Re-establish routes after loading. Finish movements or use Hilfsauflösung before unloading when retaining locks matters.
- Turntables are excluded because switch locks cannot secure their moving connection.
- Path search returns up to 32 alternatives with bounded work. Truncated searches are identified; use closer endpoints to inspect more paths.
- Changing a signal layout during an active route holds signals at stop. Release routes before changing packs or track mods.
- Both assemblies compile against game DLLs without warnings. The source-linked harness passes 110 backend assertions, including actual aspect-selection rules, through shunting heads, multi-head junctions, manual shunting, switch release before the destination, occupancy/body clearance, newly generated wagons and multiplayer snapshots. The DOM frontend harness passes 46 assertions, including manual signal controls, a 5,004-signal viewport test, occupancy canvas ordering, vehicle visibility and checks that unchanged updates preserve markers and overlays.
- The native regression harness reproduces the rejected 1.4.0 getter patch using the installed Harmony 2.3.6 DLL, then passes 226 assertions across 23 real DV Signals aspect types. It checks corrected stop/shunting metadata, exclusion of substitute permission, steady/blinking repeater colors and permission-condition patches without Unity rendering.
- In-game verification by the contributor of earlier builds covers signal rendering, vehicle clearance, manual shunting, auxiliary release, multiplayer synchronization and map responsiveness. The 1.4.3 fixes have automated regression coverage; live rendering and a two-player session remain to be verified.

## Building

Copy `Directory.Build.targets.EXAMPLE` to the ignored `Directory.Build.targets` file and set `DvInstallDir` to your game installation. `DvSignalsDir` defaults to its `Mods\DVSignals` directory and can be overridden for a separate copy of the signal assemblies. Game and mod DLLs stay local and are never included in the ZIP.

The mod and native regression harness target .NET Framework 4.8. The source-linked backend harness requires the .NET 10 SDK; the frontend harness requires Node.js 18 or newer. Restore packages before building.

```powershell
Copy-Item Directory.Build.targets.EXAMPLE Directory.Build.targets
# Edit Directory.Build.targets with your installation paths before building.
dotnet build Multiplayer\RemoteDispatch.Multiplayer.csproj -c Release
.\package.ps1
dotnet run --project tests\RouteTests.csproj
dotnet build tests\native\NativePatchTests.csproj -c Release
.\tests\native\bin\Release\net48\NativePatchTests.exe '<game installation>' '<DVSignals directory>'
npm ci --prefix tests
npm test --prefix tests
```

Tests run the actual route manager with game fakes, native aspect metadata/patches with the installed DLLs, and the frontend with a DOM harness; they do not simulate Unity rendering, physics or MPAPI transport. `node tests\preview-server.cjs` serves a browser-only demonstration at `http://127.0.0.1:8724`, without controlling the game.

## Credits

Original Remote Dispatch by Zeibach / mspielberg, under the included MIT license. DV Signals by Wiz and B0SS.

Icons made by [Freepik](https://www.freepik.com) from [Flaticon](https://www.flaticon.com/).
