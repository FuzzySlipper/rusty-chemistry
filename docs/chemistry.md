# Chemistry: a voxel reaction kernel

> The content decides what things are. The kernel decides how they change.

This repository is a lab for a Breath of the Wild / Magicka style systemic
interaction layer: fire that spreads through grass and burns it to ash, smoke
that fills a cave but clears in the open, lightning that runs through a puddle,
spells that change what the world is made of. The goal is an architecture in
which those interactions are *derived* from a small fixed set of operators and
authored data, never written pair by pair.

Two products are expected to consume it:

- **rusty-craftsurvive**: survival, magic and combat interacting through
  physics and chemistry. A felled tree hits an enemy, a grass fire damages and
  destroys, smoke punishes enclosed spaces.
- **A wardens-circuit magic sandbox**: the feel test described in
  `wardens-circuit/collab/implementation-briefs/channel-composition-feel-test.md`,
  the six Q90 channels on a voxel substrate with a vivid/muted Emperor toggle.

The two share everything up to the content pack and diverge after it.

## Why operators, not pairs

Breath of the Wild's chemistry engine (Fujibayashi, GDC 2017) rests on three
rules: elements change the state of materials, elements change each other, and
materials never change each other directly. That replaces an N×N table of
object pairs with elements acting on material *properties*. Sandboxels shows
both halves: its temperature thresholds (`tempHigh` → `stateHigh`) scale well,
and its pairwise `reactions` tables are where its content becomes unmanageable.

The kernel goes one step further. Its code is a fixed set of six operators, and
everything else, including what "fire" is, is content:

| Operator | What it does | craftsurvive | wardens-circuit |
| --- | --- | --- | --- |
| **Transform** | changes a cell in place: quantities convert (reactions) and identity switches on thresholds | combustion; grass → burning grass → ash | Fire: identity-altering transition |
| **Transfer** | moves a quantity between neighbouring cells by conductance, with optional drift | heat, smoke and moisture spreading; wind carrying smoke | Water: transfer without identity change |
| **Discharge** | an instantaneous burst through every connected conducting cell | lightning, a shock through a puddle | Thunder: impulsive discharge |
| **Propagate** | a front that travels outward at a speed, around obstacles, decaying | a blast or gust | Wind: penetrating wave propagation |
| **Confine** | a difference in a hold quantity closes faces to transfer, discharge and fronts | sealed rooms, containers | Lake: confinement, bounded hold |
| **Bind** | a quantity that resists identity change | stone versus grass, warded wood | Mountain: bound state, identity stability |

The wardens-circuit Layer 1 element behaviours are this table almost word for
word (`canonical/spell-channel-system-spec.md` §7.6). That is the strongest
evidence that the operator set is the right abstraction rather than one
invented for craftsurvive. The acceptance test for the kernel is that both
content packs run on it unchanged, and that adding an element (oil, wind
spreading fire, a seventh channel) is a content change, not a code change.

## Layers

```text
Content pack (data)        quantities, materials, reactions, transforms
        │
Kernel (pure C#)           sparse chunked cell field · six operators · emitters · modulators
                           fixed double-buffered step · active set · seeded draws
        │  events: material changed, discharged, front arrived      samples: cell state
        │
Product adapters           voxel bridge     voxel block ↔ material; identity changes written back
                           ambient bridge   weather, water, snow, sky exposure → ambient, drift, sources
                           entity bridge    entities sample overlapped cells → exposure facts; can emit
                           physics bridge   fronts/discharges → Dynamics impulses; impacts → stimuli
                           presentation     events → particles, audio; a debug overlay
        │
Product consumers          health, AI, survival, spell resolution. They react; the kernel never calls them.
```

The kernel has no Engine dependency, so it is checked headless and can be
linked by any product. Adapters are product code and use the Engine's safe SDK.

### Content

A pack declares:

- **Quantities**: continuous values each cell holds (heat, moisture, smoke,
  fuel, charge; or a channel's medium). Each has an ambient value, bounds, how
  fast it settles back toward ambient, and optionally how it transfers.
- **Materials**: cell identities. Each lists its conductance per quantity
  (0..1), the quantities it starts with, and whether the sky passes through it.
- **Reactions**: continuous local conversion, for example "burning grass
  turns 1 fuel/s into 1200 heat and 2 smoke". These are limited by what is
  available, so nothing goes below its bound.
- **Transforms**: discrete identity changes when local conditions hold,
  optionally probabilistic (`chance` per second) and optionally resisted by a
  quantity. Hysteresis is two rules with different thresholds (ignite at 250,
  douse when moisture passes 0.5), not a special mechanism.

```json
{
  "quantities": [
    { "id": "heat", "ambient": 20, "min": -50, "max": 1500, "settle": 0.05, "epsilon": 0.01,
      "transfer": { "rate": 3 } },
    { "id": "smoke", "max": 10, "transfer": { "rate": 2, "drift": true, "rise": 0.5 } },
    { "id": "fuel" }
  ],
  "materials": [
    { "id": "air", "sky": true, "conduct": { "heat": 0.05, "smoke": 1 } },
    { "id": "grass", "sky": true, "conduct": { "heat": 0.1, "smoke": 0.5 }, "initial": { "fuel": 6 } },
    { "id": "burning-grass", "sky": true, "conduct": { "heat": 0.1, "smoke": 0.5 } },
    { "id": "ash", "sky": true, "conduct": { "heat": 0.05, "smoke": 0.5 } }
  ],
  "reactions": [
    { "id": "combustion", "in": ["burning-grass"], "rate": 1,
      "consumes": { "fuel": 1 }, "produces": { "heat": 1200, "smoke": 2 } },
    { "id": "smoke-vents", "when": [{ "open": true }], "rate": 2, "per": "smoke",
      "consumes": { "smoke": 1 } }
  ],
  "transforms": [
    { "id": "ignite", "from": ["grass"], "to": "burning-grass",
      "when": [{ "quantity": "heat", "atLeast": 250 }], "chance": 3 },
    { "id": "burn-out", "from": ["burning-grass"], "to": "ash",
      "when": [{ "quantity": "fuel", "atMost": 0 }] }
  ]
}
```

The reader is strict. An unknown property, an unresolved id or an inverted
bound is reported with its path (`transforms[0].to: unknown material
'burnt-grass'`) instead of being silently dropped.

### The step

The kernel advances by a fixed step (0.1 s by default). An adapter accumulates
the Engine's admitted update time and calls `Step` as many times as it covers.
That is pacing, not a second clock. Each step runs in phases:

1. **Emitters** deposit or drain their quantity over their shape.
2. **Transfer** diffuses and drifts every transferring quantity across cell
   faces, reading the current buffer and writing the next.
3. **Reactions** convert quantities inside each cell.
4. **Settle** relaxes quantities toward ambient.
5. **Transforms** switch identities, at most one per cell per step.
6. **Discharges and fronts** resolve and advance.

The one rule that keeps this order-independent: **all spatial interaction goes
through transfer, discharge and propagation.** Reaction and transform
conditions read only their own cell. Fire spreads because heat transfers, not
because a rule looks at a burning neighbour. Within a phase, results don't
depend on iteration order, and the chance draws are keyed by seed, step, cell
and rule, so a field replays identically.

Transfer is explicit diffusion. Per face, the coefficient is the quantity's
rate × the lower of the two cells' conductances × any modulator. Drift is
upwind advection: the field's wind for quantities marked `drift`, plus the
quantity's own `rise` (buoyancy, upward in cells per second, or downward when
negative), so smoke climbs and pools under whatever roofs it. Each is clamped to 1/12 of a cell's value per face per step,
so a cell's faces can never take more than it holds. Transfer therefore stays
stable and non-negative, and is deliberately slow: at the default 0.1 s step,
drift tops out around 0.8 cells/s. Fast movement (blasts, gusts) is a front's
job.

A face between cells whose blocking hold differs is closed by that
difference. A uniformly held region flows freely inside, and its edge
confines.

A face to a cell outside the loaded region exchanges with the ambient (hold
included), at the conductance of the cell inside. So the region's edge is
open, and a test or adapter that wants a closed boundary loads a
non-conducting layer.

Only **active chunks** (16³ cells) are processed. A chunk wakes when it is
written, overlapped by an emitter, reached by a discharge or front, or
receives flux from an active neighbour. It sleeps after a few steps in which
nothing in it changed and no transform was pending. A grass field costs
nothing until it catches fire.

### Emitters and modulators: the delivery layer

**Emitters** are how anything outside the field acts on it. A torch, rain, a
chimney and a spell are all emitters. Each one has:

- a shape (a sphere or a box);
- a quantity it deposits (positive) or drains (negative) per second;
- optionally a radial drift that gathers the quantity toward its centre or
  scatters it outward;
- a lifetime, until it is removed.

One-shot **discharges** and **fronts** are started at a cell.

wardens-circuit's Layer 2 delivery modes are emitter settings:

| Delivery mode | Emitter setting |
| --- | --- |
| Heaven-upper: arrives as a field condition | an area emitter |
| Heaven-lower: acts outward from a base | outward radial drift or a front |
| Earth-upper: absorptive pressure | a draining emitter |
| Earth-lower: draws inward and concentrates | inward radial drift |

The ledger's open candidate dimensions for Layer 3 (push/pull, excess versus
deficit, duration/shape) map onto emitter sign, which side of ambient a rule
acts on, and emitter lifetime and shape. They are a concrete starting point
for the "Layer 3 → voxel parameters" item the brief is waiting on.

**Modulators** are named multipliers on rule rates (`"modulator": "fire"`)
that the adapter sets: rain damping combustion, the Emperor making a channel
vivid or muted, a celestial wax and wane. They scale and never gate, which is
what wardens-circuit's non-gating rule requires (spec §8). A global **drift**
vector (the wind) applies to quantities whose transfer is marked `drift`.

## Substrate: coarse voxel cells

Both use cases favour voxel cells. The chemistry grid is coarser than the
render voxels and sparse. The adapter loads chunks around what matters (the
player, an active spell) and maps each block to a material when loading.

- **Write-back**: identity changes that matter visually or physically (grass
  → ash, wood → charcoal, ice → water) are batched per step into the product's
  own voxel edit path. In craftsurvive that is `TerrainEditTransaction`, so
  saves and the edit overlay keep working. Transient quantities (heat in
  flight, smoke) are never voxel state.
- **Sky exposure** is a cell flag. `MarkOpenColumns` derives it from
  materials' `sky` for loaded columns, and an adapter that knows better (an
  unloaded roof overhead) sets it directly. When a transform changes whether
  a cell lets the sky through (a blasted roof), the adapter calls
  `MarkOpenColumn` for that column. A reaction on open cells drains
  smoke, so **enclosed spaces fill with smoke and open ground clears it with
  no room detection at all.**
- **Entities** don't live in the field. The entity bridge samples the cells an
  entity overlaps and reports exposure (heat, smoke, charge) to that entity's
  owners. The health system turns heat into damage, and AI turns smoke into
  flight or blindness. A burning creature or a held torch is an emitter that
  moves with it.

## Physics and the Engine boundary

The rules, packs and kernel stay downstream. Which things are flammable and
what electricity does is product meaning, and if several products want the
kernel, a shared library serves them better than the Engine. The Engine
already owns what the physics bridge needs on the *push* side:

- Dynamics impulses at a point drive blasts and gusts, using the arrival
  cells and directions a front reports.
- Bodies float in water fields.
- Spatial overlap queries find the entities in a cell.

What is missing is the *structure* side. Verified against the Engine docs on
main; to be re-verified against the consuming product's pin before filing:

1. **Detaching voxels into a dynamic body.** Dynamics bodies are boxes,
   spheres and capsules, and nothing turns a connected voxel region into a
   body or finds regions that lost their support. craftsurvive's
   `TreeFelling` clears a felled trunk in the edit's own transaction; nothing
   falls. "Tree bonks enemy", supports burning out, and Mountain/Bind losing
   hold all need this. For trees alone, an honest product-side step exists:
   craftsurvive knows each tree's shape from its recipe and can spawn a
   capsule log body with a log mesh. General collapse is the Engine request.
2. **Impact readouts.** The relative velocity or impulse of a contact, so a
   falling log becomes damage. Not yet confirmed either way.
3. **Only if profiling demands it**: a native multi-channel cell field on a
   worker, following `engine.WaterField`. The C# kernel comes first.

## craftsurvive's existing environment systems

**Draw on them, don't absorb them.** They model the world at a different
scale. The weather is regional, deterministic from seed and clock, and mostly
unsaved. River stage is per reach in game hours. Chemistry is local (metres),
fast (seconds) and stateful. Folding them into the kernel would turn it into a
climate model or force weather to tick at fire rates. Each is already the one
owner of its domain, and the kernel reads them as ambient inputs through the
ambient bridge:

| System | What chemistry takes from it | Direction |
| --- | --- | --- |
| `WeatherField` → `EnvironmentChannels` | `Cold` → ambient heat; `Precipitation` → a moisture emitter over open cells; `Wind` + the flow direction → drift; `Murk`/`Arcane` → candidate modulators | read |
| `WindSway` | the scene wind's direction at a time, so smoke drifts the way the grass leans | read |
| `WaterModel` (+ `RiverStage`, `Floods`) | where water stands: submerged cells load as a water material and quench heat. Chemistry never simulates hydraulic flow | read |
| `SnowCover` | snow depth on surface cells → moisture and cold. A fire melting snow is an event the snow owner applies to its own saved cells | read, and events to its owner |
| `EnvironmentEffects` (`Wetting`, `Chill`, `Harm`, `Sight`) | its doc says a new environmental system adds to it rather than to its consumers. Local chemistry exposure should reach health and sight the same way; its per-game-hour rates may need a real-time sibling | publish |

Patterns worth reusing rather than reinventing:

- `SnowCover`'s residency: settle a cell on first request, keep cells near the
  party, forget the oldest past a cap.
- Engine-free rule files checked headless (`tests/RpgCore`).
- Keyed deterministic draws (`KeyedDraws`).

What chemistry would newly own in craftsurvive is the *local, fast* layer that
none of these model: surface wetness after rain or a splash, heat around a
fire, smoke in a room, charge in a puddle.

## The wardens-circuit sandbox

What maps directly:

- **Layer 1**: the six operators.
- **Layer 2**: emitter delivery.
- **Emperor vivid/muted**: a modulator.
- **Environmental cues** (fire glow, flooding, overgrowth, stress lines,
  sediment) and **symptom severity**: materials and quantities in the field
  that channels act on.

Whether a channel fits a symptom can then be *read from the simulation*, as
the canon's "derived, not authored" rule asks. The canon also says casts
change flows, bindings and identity rather than adding to the ambient
five-phase medium. That is a content choice: the Q90 pack simply has no
emitter that deposits the medium.

What stays out of the kernel:

- the hexagram XOR fit;
- routing permutations;
- the room state machine (JAMMED/RELIEVING/RESTORED/WORSENED);
- Layer 3 text.

That is wardens game logic that reads the field. The no-numbers HUD rule
applies to players; the debug overlay is a developer tool.

Questions for the wardens-circuit author, not ours to settle:

- The class trinity tables in the spec (§4.2) and onboarding (§6.1) disagree.
- The projectile brief resolves a Water symptom with Hex 33, which has no
  Water trigram.

## Plan

1. **Kernel**: operators, rules reader, step, active set, emitters, with
   focused headless checks (`tests/ChemistryKernel`). *Done; see below.*
2. **Sandbox here**: an Engine voxel arena, a debug overlay, and the physical
   pack: grass fire, a smoky cave, water dousing, a shock in a puddle. *Done;
   see [sandbox as built](#sandbox-as-built).*
3. **Q90 pack**: six channels and the Emperor toggle, the brief's feel test,
   on the same kernel with no kernel change.
4. **Engine request** for voxel detachment and impact readouts, after
   re-verifying against the pin. Filing needs the owner's go-ahead.
5. **craftsurvive adapters**: voxel, ambient, entity and physics bridges.

Open questions:

- Material movement (falling sand, pouring) isn't an operator. Water's
  hydraulics belong to the water owners; whether anything else needs moving
  matter is unknown.
- Cell size relative to craftsurvive's voxels, and the residency radius.
- Whether the kernel ships to products as a project reference, a package, or
  a vendored copy.

## Kernel as built

`src/RustyChemistry/` contains:

- `Content/`: definitions and the strict JSON reader.
- `Simulation/`: the field, its chunks, emitters, fronts and events.

Adapters read the field in bulk: `LoadedChunks` lists chunk origins,
`IsActive` says whether a chunk is still changing (a sleeping chunk's values
hold still), and `ReadChunk` and `ReadChunkMaterials` copy a chunk's values
or materials, indexed x + 16y + 256z.

The kernel project is compiled optimized in every configuration. Its loops
run over every cell of every active chunk and are about eight times slower
unoptimized, and `rusty dev` builds Debug.

The kernel works in cell space. Emitter shapes, drift and front speed are in
cells and cells per second; `CellAt` and `WorldCenter` convert using the
settings' cell size and origin. Events are lists that the next `Step`
replaces: `MaterialChanges`, `Discharges` and `FrontArrivals`.

`tests/ChemistryKernel/` checks each operator's semantics on small authored
packs:

- conservation, and the open edge;
- fire spreading, stopping at stone and burning out;
- hysteresis;
- smoke trapped indoors and venting outdoors;
- buoyant smoke pooling under a roof, and a roof opening only its column;
- discharge limited to a connected puddle;
- a front bending around a wall;
- Confine and Bind;
- modulators;
- sleep;
- replay determinism;
- reader errors.

```sh
dotnet run -c Release --project tests/ChemistryKernel
```

The checks end by timing a 64×64 meadow fire on a stone floor: 16 chunks, all
active while it burns. On the development machine it takes about 1.7 ms per
step, so about 17 ms per simulated second at 10 Hz. That is a record, not a
gate.

Known limits of this first cut:

- An active chunk processes all 4096 cells. The only shortcut is skipping
  faces between equal values. Dense air is the main cost, and per-quantity
  activity or a narrower active set is the next lever if adapters need more.
- There is no unload or residency API yet. Chunks are loaded and stay.
- A cell changes identity at most once per step. Emitter shapes are spheres
  and boxes.
- Transforms keep a cell's quantities except those they `set`. Loading a cell
  resets them to the material's initial values.

## Sandbox as built

`src/ChemistrySandbox/` is the Engine product (`rusty dev`). It draws a
64×32×64 arena one voxel per cell and steps the field on the Engine's admitted
simulation time, at most three steps per update. Time beyond those is
dropped, not owed.

- `Arena/ArenaLayout.cs` builds the field. It has a meadow on dirt and
  bedrock, plus a set piece for each behaviour the pack should show:
  - a stone path three cells wide, as a firebreak;
  - a pond and a separate puddle;
  - a stone hut with a doorway and a wood campfire inside;
  - a wooden shed;
  - trees of wood and leaves.
- `Arena/VoxelMirror.cs` gives each material with a look a voxel slot and
  writes each step's `MaterialChanges` back as voxel edits.
- `Overlay/QuantityOverlay.cs` shows one quantity as vertex-coloured cubes,
  one mesh per field chunk, rebuilt only when that chunk's values change by a
  display level:
  - in open cells, cubes grow with the value;
  - around opaque voxels, a tinted shell shows the value inside;
  - inside translucent voxels (water), cubes are opaque, so they draw the same
    under the surface in every chunk.
- `Tools/ToolBelt.cs` turns a click into an emitter, a discharge or a front.
- `Viewing/OrbitCamera.cs` orbits, pans and zooms, and turns the cursor into
  a pick ray for `Spatial.PickVoxel`.
- `Ui/` and `src/ui/main.js` form the panel. It chooses the tool, overlay and
  wind, pauses, steps or resets the field, and shows step cost and material
  counts.

Content is in two files. `content/chemistry/physical.json` is the pack.
`content/sandbox/sandbox.json` holds each material's colour, emission and
translucency, the overlays' colours and ranges, the tools' parameters and the
wind speed.

Live-debug commands act on exact cells, for agents and playtests:

- `sandbox.use <tool> x y z` uses a tool on a cell;
- `sandbox.cell x y z` reads its material, sky exposure and quantities;
- `sandbox.stats` reports the step, activity, cost and material counts.

The `rusty-chemistry-hosted` playtest profile starts a private host per
session from `.den-serve.json`.

What the first playtest showed, on the development machine:

- **Grass fire** spreads and burns to ash. It stops at the stone path, and
  the wet pond shore resists it.
- **Water** douses the cells it reaches. A fire front already past the splash
  keeps going.
- **The hut** fills with smoke from the campfire: about 2.1 under the roof,
  0.5 at mid-height and 0.2 in the doorway, against about 0.005 outside.
- **A lightning strike** on the pond charges the whole pond (53.6 per cell).
  The separate puddle and the dry shore stay at 0.
- **A blast** on the shed roof breaks one roof cell and opens its column to
  the sky. A fire lit inside then vents through the hole.
- **An east wind** leans every smoke plume toward +X.
- **Cost:** during a large burn, 18 active chunks cost about 20 ms per step.
  That is about five times the headless meadow per chunk, and the cause isn't
  profiled yet. Rise isn't it: headless, it adds under 10%. The host also
  shares the machine with its renderer. This is the first thing to profile
  if adapters need more.
- **Content bug found:** `burning-wood` didn't conduct smoke, so its smoke
  stayed inside it, capped at the maximum. A rule producing a transferring
  quantity in a material that can't pass it on is easy to write. The reader
  could warn about it.

Limits:

- Smoke inside the hut is invisible from outside, so the readings come from
  `sandbox.cell`. A cutaway view would show it.
- Nothing here moves bodies yet. Physics waits for step 4.

