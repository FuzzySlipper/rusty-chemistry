# Product architecture

> The product decides. The Engine guarantees.

```text
Chemistry kernel (Engine-free C#) -> sandbox adapters and policy (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, UI transport, and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyChemistry/` | The chemistry kernel: pack reader, field, step, emitters, discharges, fronts |
| `src/ChemistrySandbox/SandboxProduct.cs` | Lifecycle, step pacing on admitted time, input and command interpretation, debug commands, panel projection |
| `src/ChemistrySandbox/Arena/` | The arena's layout, and the voxel mirror that writes identity changes back as Engine voxel edits |
| `src/ChemistrySandbox/Overlay/` | Drawing one quantity as per-chunk meshes |
| `src/ChemistrySandbox/Tools/`, `Viewing/`, `Setup/`, `Ui/` | Tools, the orbit camera and picking, typed sandbox content, the panel contract |
| `src/ChemistrySandbox/ChemistrySandbox.csproj` | Explicit product entry, content/UI roots, the command intent, and host defaults |
| `src/ui/main.js` | DOM panel, command claims, projection subscription, and UI cleanup |
| `content/` | The physical pack and the sandbox's looks, overlays, tools and wind |
| Engine SDK/runtime | Generated interop, admitted updates/input, retained UI transport, host, renderer, and browser shell |

## Lifecycle and data flow

The installed runtime loads the product assembly through its SDK-generated bind
entry point. The bind checks
the SDK/runtime ABI identity and constructs the product with
`ProductCreateContext`. The product opens its UI stream through `IEngineContext.Ui`.

Engine calls `Start` and admits `ProductUpdate` callbacks. Each update
interprets pointer and keyboard input (camera, picking, tool keys) and panel
commands (the `sandbox.command` payload intent), then steps the field for the
admitted simulation time, at most three steps. Each step's identity changes
become voxel edits and the overlay redraws the chunks that changed. C#
publishes a `UiValue` for the panel, which holds no authoritative state.

Engine owns pause/resume/restart/shutdown admission. Restart rebuilds the
arena. Disposal releases the overlay meshes, voxel scene, camera and UI
stream. Resource lifetimes and admitted update facts remain Engine-owned.

## Build and host

`Directory.Build.props` pins one immutable SDK/runtime pair. The Engine `rusty`
command installs it into its shared cache (`rusty install`), supplies its
package source to restores, and runs the product on its runtime (`rusty dev`,
which owns staging, watching, worker replacement and serving). `rusty build`
stages CoreCLR through `StageRustyEngineCoreClrProduct`; `rusty build --aot`
runs `VerifyRustyEngineAot` for explicit fidelity/release checks. Generated
bindings and the bind entry point are ignored output, never edited sources.
The product supplies only its C#, DOM UI and content; browser assets and host
binaries stay in the Engine runtime. The `engine-pair` workflow advances the
pin only after the product builds and serves on the new pair.

Before adding a mechanism, check both the installed safe SDK and the owners
above. Product meaning stays downstream. A missing Engine capability is an
upstream request, not another local host, transport, scheduler, or renderer.
