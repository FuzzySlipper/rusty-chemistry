# Rusty Chemistry

A lab for a modular, Breath-of-the-Wild-style chemistry kernel on voxel cells
([docs/chemistry.md](docs/chemistry.md)). The Engine-free kernel lives in
`src/RustyChemistry/`, and its headless checks run with
`dotnet run -c Release --project tests/ChemistryKernel`.

The product is a sandbox with two scenes. The physical arena runs the
kernel's physical pack: light grass, douse it, strike the pond with
lightning, blast the shed and watch the smoke. The Q90 machine room casts
wardens-circuit's Quadrant 90 channels, composed from their trigrams with
placeholder tables, at symptoms in a sick machine. The packaged Engine owns the host, input, update
loop and browser shell. The repository started from rusty-template.

## Setup

Engine pairs target Linux x64 and Windows x64. Install the .NET 10 SDK, `curl`
and `tar`. NativeAOT also needs the platform compiler/linker prerequisites
(Clang and zlib development headers on Linux). Get the Engine's `rusty`
command once:

```bash
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
```

On Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.ps1 | iex
```

Then, from this repository:

```bash
rusty status
rusty install
rusty dev --port 8787
```

Open the URL printed by the host. Pick a tool in the panel and click the
arena; the panel lists the camera controls.
`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

`Directory.Build.props` pins the exact SDK/runtime pair and names the product
project (`<RustyEngineProject>`) that `rusty dev` and `rusty build` use without
`--project`. `rusty install`
downloads it once into the shared Engine cache, and later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build
```

`rusty update` lists the release notes to read; include the changed
`Directory.Build.props` in the resulting source change. This repository's
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read. For an explicit
NativeAOT fidelity/release check:

```bash
rusty build --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyChemistry/` | Engine-free chemistry kernel: pack reader, field, step |
| `src/ChemistrySandbox/` | The sandbox product: scenes, layouts, voxel mirror, overlay, casting, channels, camera |
| `src/ui/main.js` | DOM panel: tool, overlay and wind choices, field controls and stats |
| `content/` | The physical and Q90 packs and each scene's looks, overlays and tools or channels |
| `tests/ChemistryKernel/` | Headless checks of the kernel's operators |
| `Directory.Build.props` | Matched Engine SDK/runtime pin and default product project |
| `docs/architecture.md` | Current ownership and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
