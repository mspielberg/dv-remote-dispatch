# Contributing to RemoteDispatch

Thanks for your interest in contributing. This document covers how to get set up, how the codebase is organised, and what to keep in mind when submitting changes.

---

## Project overview

The three main moving parts:

- **The game watchers** (`Engine/`) hook into Unity events and Harmony patches to detect when something changes.
- **The session system** (`Server/Session.cs`, `Server/AsyncSet.cs`) tracks which data each connected client (web browser session) still needs to receive, using a tag-based long-poll mechanism rather than constant polling.
- **The HTTP server** (`Server/HttpServer.cs`) serialises game state to JSON and pushes it out to the client, and routes incoming control commands back into the game on the main thread.

There is also an optional **Signals sub-project** (`RemoteDispatch.Signals/`) — a separate assembly that integrates with the [DVSignals](https://github.com/Fuggschen/dv-signals-test) mod if it is installed. It is loaded at runtime via reflection by `SignalsShim.cs` in the main project, so the core mod has no compile-time dependency on it.

---

## Setting up

### Prerequisites

- [Derail Valley](https://www.derailvalley.com/) with [UnityModManager](https://www.nexusmods.com/site/mods/21) installed
- [dotnet SDK](https://dotnet.microsoft.com/download) version 10 (recommended — solves most build issues)
  (`dotnet --version` to confirm it's on your PATH)

#### Optional, but recommended

- **[Windows only]** [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/) is free for open source use.
  During installation, select the **.NET desktop development** workload — this includes the dotnet SDK and everything else needed to build the mod.
- **[All platforms]** [Visual Studio Code](https://code.visualstudio.com/) with the [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit), pointed at the `.slnx` file.

### Building for the first time

1. Clone the repository.
2. Create a `Directory.Build.props` file in the root of the repo from the example below. This is a personal file and should not be committed back to the repo:
    ```xml
    <Project>
        <PropertyGroup>
            <!-- Replace this with the path to Derail Valley on your machine -->
            <DvInstallDir>C:\Program Files\Steam\steamapps\common\Derail Valley</DvInstallDir>
        </PropertyGroup>
    </Project>
    ```
3. With your terminal in the root of the repo (same directory as the `RemoteDispatch.slnx` file), run:
    ```
    dotnet build -v detailed
    ```
4. You should see output like this:
    ```
    Restore complete (0.3s)
        Determining projects to restore...
        All projects are up-to-date for restore.
      RemoteDispatch.Signals netstandard2.0 succeeded (0.1s) → RemoteDispatch.Signals\bin\Debug\netstandard2.0\RemoteDispatch.Signals.dll
      RemoteDispatch netstandard2.0 succeeded (0.8s) → RemoteDispatch\bin\Debug\netstandard2.0\RemoteDispatch.dll
        Deployed to: C:\Program Files\Steam\steamapps\common\Derail Valley\Mods\RemoteDispatch

    Build succeeded in 1.4s
    ```
5. Go into your RemoteDispatch mod folder (`<DerailValley>/Mods/RemoteDispatch`) and make sure there is no `.cache` file — delete it if there is one, as it will be an older version of the mod.
6. Run the game and make sure RemoteDispatch is enabled in UMM.

#### Linux users

The build process includes a package script that will not run automatically on Linux. You will need to manually copy the `.dll` files to your mods folder.

Make sure your `Directory.Build.props` points at the `Managed` folder within your Derail Valley installation for dependency resolution — the path will differ from the Windows example above. Installing PowerShell Core and running the package script manually is also an option. If you'd like to contribute a change to `RemoteDispatch.csproj` to handle this automatically on Linux, that would be very welcome.

#### Release builds

Run `dotnet build -c Release -v detailed` and the package script will create a zip file in a `dist` folder.

---

## Code organisation

```
RemoteDispatch/
├── Main.cs            # Mod entry point, lifecycle (enable/disable/reload)
├── Settings.cs        # Port, password, per-user permissions, in-game GUI
├── Engine/
│   ├── CarUpdater.cs       # Harmony hooks on car spawn/despawn and control changes
│   ├── JunctionPatches.cs  # Harmony hook on Junction.Switch
│   ├── LocoControl.cs      # Sends throttle/brake/reverser commands to RemoteControllerModule
│   └── Updater.cs          # Unity coroutines; RunOnMainThread bridge for async code
├── Data/
│   ├── CarData.cs          # Snapshots car and loco state to JSON-serialisable objects
│   ├── JobData.cs          # Job and task data; Harmony hooks on job state changes
│   ├── PlayerData.cs       # Player position and Steam name
│   └── RailTracks.cs       # Track geometry and junction positions, baked to lat/lon
├── Server/
│   ├── AsyncSet.cs         # Thread-safe set with async TakeAsync, used for pending tags
│   ├── HttpServer.cs       # HttpListener; routes requests, handles auth and gzip
│   └── Session.cs          # Per-client sessions; tag-based change notification
├── Shims/
│   └── SignalsShim.cs      # Optional runtime integration with the DVSignals mod
└── frontend/
    └── *                   # Frontend source (see below)

RemoteDispatch.Signals/     # Separate assembly, only loaded if DVSignals mod is installed
├── Bootstrap.cs            # Public entry point called by SignalsShim via reflection
└── SignalsBridge.cs        # Stubs for reading/writing signal aspects
```

### The frontend

The frontend is a JavaScript application that lives in `frontend/`. It is **packaged directly into the main DLL at build time** — there is no separate frontend build step to run, and no dev server. Any changes you make to the frontend files will be picked up automatically the next time you run `dotnet build`.

---

## How the update system works

Understanding this makes it much easier to add new data types.

When something in the game changes, the relevant watcher calls `Sessions.AddTag("some-tag")`. This queues the tag in every active client session's `AsyncSet<string>`. Clients long-poll `GET /updates/<sessionId>` — if there are pending tags they get them immediately; otherwise the request suspends for up to one minute waiting.

When the server assembles the response, it calls `GetUpdateForTag(tag)` to serialise the current state for each pending tag into a JSON object, keyed by tag name.

To add a new data type:

1. Add a serialiser in `Data/` that returns a `JObject` or `JToken`.
2. Add a case to `GetUpdateForTag` in `Session.cs`.
3. Add whatever game-side watcher calls `Sessions.AddTag("your-tag")` when the data changes — either a Harmony patch in `Engine/`, an event subscription in `CarUpdater.cs`, or a coroutine in `Updater.cs`.
4. Add an HTTP endpoint in `HttpServer.cs` if you also want clients to be able to fetch the data on demand (outside of the update stream).

---

## Harmony patches

All patches are applied when the mod is enabled and removed when it is disabled or toggled off.

If you add a new patch class, place it in the file most relevant to what it's patching (`CarUpdater.cs` for car/loco patches, `JunctionPatches.cs` for junction patches, `JobData.cs` for job patches).

---

## The optional Signals integration

`SignalsShim.cs` loads `RemoteDispatch.Signals.dll` at runtime via reflection if the DVSignals mod is present. The main project has no compile-time dependency on Signals. If your change touches signal-related behaviour, it belongs in the separate Signals integration assembly, not in the core project.

---

## Debugging and logging

- `Main.Log(...)` — always writes to the UMM log.
- `Main.DebugLog(...)` — only writes logs when the logging setting is enabled in the UMM mod settings; use this for noisy or diagnostic output you want users to be _able_ to use, but not be noisy if they are debugging another mod.

You can also "gate" your log commands with the preprocessor directive `#if DEBUG`, this will mean code within the gate will only be included in the build if you built the code in DEBUG configuration. This is the default if you do not pass `-c Release` to the `dotnet build` command.

---

## Code style

There is an `.editorconfig` at the root of the repo — make sure your editor respects it. The main things to be aware of:

- **Indentation:** tabs, not spaces. This lets each contributor set their editor to display whatever indent width suits them, and avoids large whitespace-only diffs in PRs.
- **Brace style:** Allman for C# (opening brace on its own line), K&R One True Brace Style for JavaScript (opening brace on the same line). Most editors default to these and will apply them automatically.

Following these conventions keeps PR diffs clean and makes review easier for everyone.

---

## Testing

All testing is manual — there are no automated tests (side note, if you want to add automated tests _please do_). Before submitting a PR, please test your changes in-game with at least one connected browser client. Things worth exercising:

- Does your new feature/bugfix work
- The update loop (does data reach the client when something changes in-game) still functions

---

## Submitting changes

- **Keep pull requests focused.** One feature or fix per PR makes review much easier.
- **More smaller changes over few large ones.** If you have a big change in mind, consider breaking it down into smaller, incremental PRs. This makes it easier to review and merge, and helps avoid merge conflicts. You can always gate unfinished features behind a debug setting or feature flag if you want to merge them before they're fully polished.

---

## Feature flags

If you are adding a feature that is not yet complete, or that you want to be able to toggle on and off for testing, you can gate it behind a setting in the mod settings menu. This allows other contributors to test and build on your work while it's in progress, as well as allowing users to opt in to unfinished features if they are interested in testing them.

These should not be confused with normal feature settings.
Feature flags are for gating incomplete features that are still being worked on, while normal settings are for features that are complete but that users may want to enable or disable. It is entierly possible and realistic for a feature flag to be moved to a normal setting once the feature is complete and polished.

---

## AI

Using AI to help you write the code is fine, using AI to explain the code to you is fine, using AI to write all the code is fine.

Using AI to write code that you have not read and do not understand is not fine.

Rule of thumb: *Only use AI to help you write code that you could have written without AI.* This means you can use it to speed you up, or to learn, but please do not just try to vibe-code an entire feature.
