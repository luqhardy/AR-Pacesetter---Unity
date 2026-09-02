# Embedding Unity as a Library in a SwiftUI app

*日本語版: [UNITY_AS_A_LIBRARY.ja.md](UNITY_AS_A_LIBRARY.ja.md)*

A practical guide to shipping a SwiftUI app with Unity embedded inside it (Unity as a Library, "UaaL").
Written from a working integration — every pitfall listed here is one we actually hit, not a theoretical one.

Verified with **Unity 6000.3.17f1**, **Xcode 26**, iOS device builds.

---

## 1. The mental model

The thing that trips people up first:

> **Unity is not the app. Unity is a framework the app loads.**

- The **SwiftUI app** is the host. It owns the app lifecycle, the window, navigation, and system permissions.
- **Unity** compiles to `UnityFramework.framework` and renders into a `UIView` you place wherever you like.
- Building the Unity project alone produces a Unity-only app with none of your SwiftUI screens. You always build the **host app's scheme**.

So the pipeline is: *Unity exports an Xcode project → the host app links the framework out of it → you build the host.*

## 2. Layout

```
repo/
├── Assets/ ProjectSettings/        Unity project (the repo root here)
│   └── Editor/IOSBuildExporter.cs  menu item that performs the export
├── ios/
│   ├── ARRunner.xcworkspace        ← open THIS, not either .xcodeproj
│   ├── AR_Runner_UI/               SwiftUI host app
│   └── UnityExport/                Unity's exported Xcode project (generated; gitignored)
```

The workspace exists purely to let one Xcode window see both projects, so the host can reference
Unity's build products. Keep the exported project **out of version control** — it's a build artifact,
and it's ~1.5 GB.

## 3. Step 1 — Export the Xcode project from Unity

This step runs anywhere Unity runs, **including Windows**. Only step 2 needs a Mac.

```bash
Unity.exe -batchmode -quit -projectPath <repo> \
  -executeMethod IOSBuildExporter.ExportIOS -logFile export.log
```

The exporter is a few lines around `BuildPipeline.BuildPlayer` with `BuildTarget.iOS` and a fixed
output path that the workspace expects.

**Make your exporter return a non-zero exit code on failure.** Unity's `-quit` exits 0 even when the
build failed, so CI and shell scripts silently treat a broken export as success:

```csharp
if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode)
    EditorApplication.Exit(1);
```

### Prerequisites that will fail the export

| Requirement | Symptom if missing |
|---|---|
| **iOS Build Support** module installed for your exact Unity version | Export fails immediately |
| **Player Settings usage descriptions** filled in for every API you touch (Microphone, Location, Bluetooth…) | `BuildFailedException: Microphone class is used but Microphone Usage Description is empty` |
| Several GB of free disk | Burst dies with an out-of-space IO error, leaving the project half-migrated |

The Player Settings usage strings are **separate from** the host app's `INFOPLIST_KEY_*` values.
You need both: Unity's for the export to succeed, the host's for the runtime permission prompt.

### Check `ProjectSettings.asset` afterwards

The build process can empty **`preloadedAssets`**. If that list contains `XRGeneralSettings.asset`,
committing it empty means the XR (ARKit) loader never initialises in a player build — a bug that only
shows up on device. Diff it after every export and restore if needed.

## 4. Step 2 — Wire the framework in Xcode (macOS, one-time per export)

Open `ios/ARRunner.xcworkspace`. Both projects must appear in the navigator.

1. **Host target → General → Frameworks, Libraries, and Embedded Content → `+` → `UnityFramework.framework`**
   (pick it from the Unity project's products, don't browse to a path), then set it to **Embed & Sign**.
   Leaving it "Do Not Embed" builds fine and crashes at launch with `dyld: Library not loaded`.
2. **Unity-iPhone project → `Data` folder → File Inspector → Target Membership → `UnityFramework`.**
   Without this you get undefined-symbol link errors.
3. Build the **host app's scheme**, to a **physical device**.

Redo 1 and 2 whenever you replace the exported folder. They live in the *exported* project, which you
just overwrote.

### `ld: framework not found UnityFramework`

`UnityFramework.framework` is declared with `sourceTree = BUILT_PRODUCTS_DIR` — it isn't a file on
disk, it's a build product. If nothing builds it first, the linker searches an empty directory. Fix by
adding it to **Build Phases → Target Dependencies**, or to the scheme's **Build** list ahead of the app.

### The target won't appear in any picker

Unity exports with `SUPPORTED_PLATFORMS = iphoneos` — **device only**. While a Simulator destination is
selected, Xcode filters the target out of Target Dependencies and scheme pickers entirely. Select your
physical device first. (ARKit needs a device anyway.)

## 5. Step 3 — The bridge

Two directions, two different mechanisms.

### Swift → Unity

```swift
UnityFramework.getInstance()?.sendMessageToGO(
    withName: "ARSessionManager",       // GameObject name — must match exactly
    functionName: "OnSwiftCommand",     // public method on a MonoBehaviour attached to it
    message: jsonString)
```

On the Unity side, a `MonoBehaviour` with `public void OnSwiftCommand(string json)`. Create the
receiver GameObjects at runtime so no scene wiring can rot:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Bootstrap() { /* new GameObject("ARSessionManager").AddComponent<Bridge>(); */ }
```

Send **one JSON string** with a `command` field rather than many methods — a single entry point is far
easier to version and to log.

### Unity → Swift

Unity can't call Swift directly. Go through an Objective-C++ shim in `Assets/Plugins/iOS/`:

```objc
extern "C" void UnitySendMessageToSwift(const char *json) {
    NSString *m = [NSString stringWithUTF8String:json];
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:@"UnityToSwiftMessage"
                                                           object:nil
                                                         userInfo:@{@"json": m}];
    });
}
```

```csharp
[DllImport("__Internal")] static extern void UnitySendMessageToSwift(string json);
```

Swift subscribes to that notification name. Marshal on the main queue — Unity may call from its own thread.

**Watch for duplicate symbols.** Every `.mm` under `Assets/Plugins/iOS/` is compiled into the framework.
Two files defining the same `extern "C"` function is a link error that no C# compile, unit test, or
editor Play Mode will ever catch — only a device link does.

## 6. Step 4 — Launch Unity and host its view

```swift
let bundle = Bundle(path: Bundle.main.bundlePath + "/Frameworks/UnityFramework.framework")
bundle?.load()
let ufw = (bundle?.principalClass as? UnityFramework.Type)?.getInstance()
ufw?.setExecuteHeader(...)                  // see below
ufw?.runEmbedded(withArgc: CommandLine.argc, argv: CommandLine.unsafeArgv, appLaunchOpts: nil)
```

Then wrap `ufw.appController()?.rootView` in a `UIViewRepresentable` and place it in your SwiftUI hierarchy.

### `Undefined symbol: __mh_execute_header`

Unity's sample passes `&_mh_execute_header`, a symbol the linker only provides to the main executable.
Referencing it from Swift fails to link under recent toolchains. Use dyld instead — index 0 is always
the main executable:

```swift
if let header = _dyld_get_image_header(0) {
    ufw.setExecuteHeader(UnsafeRawPointer(header).assumingMemoryBound(to: MachHeader.self))
}
```

**Pass the real pointer, never a copy.** Unity walks the load commands that follow the header in memory;
handing it a copied struct gives it unrelated heap after the first 32 bytes.

### The silent-fallback trap

Guarding with `#if canImport(UnityFramework)` and providing stubs in the `#else` branch is convenient for
simulator work — and dangerous. If the framework isn't linked, **the app builds, runs, and looks fine**
while nothing is connected. Make the fallback obviously fake: a visible "not linked" placeholder, and
never a plausible random value. We had a stub returning a random latency inside the target range, which
would have been reported as a real measurement.

## 7. Moving an export between machines

The export is large but compresses about 4:1 (`tar czf`, 1.5 GB → ~334 MB). If you create the archive on
**Windows**, execute bits on extensionless Mach-O binaries are lost, so on macOS:

```bash
chmod +x ios/UnityExport/usymtool ios/UnityExport/usymtoolarm64 ios/UnityExport/*.sh
xattr -dr com.apple.quarantine ios/UnityExport   # if downloaded
```

Without the first line you get `Command PhaseScriptExecution failed with a nonzero exit code`, because
`process_symbols.sh` can't run `usymtool`. Unity's IL2CPP phase `chmod`s its own tools, so it's unaffected —
which makes the failure look arbitrary.

## 8. Pitfalls, in one table

| Symptom | Cause |
|---|---|
| `dyld: Library not loaded` at launch | UnityFramework not set to **Embed & Sign** |
| Undefined symbols at link | `Data` folder's Target Membership isn't UnityFramework |
| `ld: framework not found UnityFramework` | Framework never built — add a target dependency |
| Target missing from every picker | Simulator destination selected; Unity exports device-only |
| `Undefined symbol: __mh_execute_header` | Use `_dyld_get_image_header(0)` instead |
| Duplicate symbol at link | Two `.mm` plugins defining the same function |
| `PhaseScriptExecution failed` | `usymtool` lost its execute bit in transfer |
| Export fails on Microphone/Location | Unity **Player Settings** usage description empty |
| Builds and runs but nothing is connected | `#if canImport(UnityFramework)` fell through to stubs |
| ARKit never initialises on device | `preloadedAssets` emptied by the export |
| `does not conform to 'ObservableObject'` | Xcode 26 no longer re-exports Combine via SwiftUI — `import Combine` |

## 9. What to automate

Almost every failure above is a compile or link error that a macOS CI runner catches in minutes without a
device or a signing certificate (`CODE_SIGNING_ALLOWED=NO`). If you're exporting on one machine and
building on another, that job pays for itself immediately.

---

See also: [SWIFT_INTEGRATION.md](../SWIFT_INTEGRATION.md) for this project's specific message contract,
and [BUILD_ON_BORROWED_MAC.md](BUILD_ON_BORROWED_MAC.md) for a one-off build on someone else's Mac.
