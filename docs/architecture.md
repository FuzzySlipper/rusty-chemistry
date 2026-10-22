# Product architecture

> The product decides. The Engine guarantees.

```text
Counter state and policy (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, UI transport, and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyTemplate.Game/Counter/CounterState.cs` | Counter value, increment saturation, and reset policy |
| `src/RustyTemplate.Game/RustyTemplateProduct.cs` | Lifecycle callbacks, semantic input interpretation, and counter projection |
| `src/RustyTemplate.Game/RustyTemplate.Game.csproj` | Explicit product entry, content/UI roots, intents, and host defaults |
| `src/ui/main.js` | DOM button/label, intent submission, projection subscription, and UI cleanup |
| `content/` | Product-authored data |
| Engine SDK/runtime | Generated interop, admitted updates/input, retained UI transport, host, renderer, and browser shell |

## Lifecycle and data flow

The installed runtime loads the product assembly through its SDK-generated bind
entry point. The bind checks
the SDK/runtime ABI identity and constructs the product with
`ProductCreateContext`. The product opens its UI stream through `IEngineContext.Ui`.

Engine calls `Start` and admits `ProductUpdate` callbacks. The DOM button
submits the declared `increment` intent. C# interprets active direct-digital
input, updates its counter, and publishes a typed `UiValue`. The DOM observes
that projection and displays its number; it holds no authoritative counter.

Engine owns pause/resume/restart/shutdown admission. Product callbacks apply
local policy, such as resetting the counter on restart. Disposal releases the
UI stream. Resource lifetimes and admitted update facts remain Engine-owned.

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
