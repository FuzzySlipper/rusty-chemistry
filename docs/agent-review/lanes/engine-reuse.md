# Lane: Engine reuse

**Always check.** Does the change recreate a mechanism already safely available
in `Rusty.Engine`, or disguise a missing upstream capability?

Trace the behavior before judging placement. Read the installed package's safe
contract and relevant Engine documentation/source; source-only capabilities
are not automatically available in the pinned package. Search behavior synonyms,
not just matching class names. Focus on families the change touches: lifecycle,
input, rendering/resources, spatial/navigation, entities/mechanics, randomness,
content, persistence, or diagnostics.

An actionable duplicate finding names:

1. The supported safe API and the package exposing it.
2. The local duplicate at file/line and its callers.
3. Which guarantee is duplicated, as distinct from product meaning or policy.
4. A concrete adoption path that preserves the product's behavior.

For a gap, name the missing purpose-neutral behavior, likely Engine owner,
search evidence, and blocked downstream behavior. File/link one upstream request
when authorized; otherwise provide request-ready wording. Never prescribe
handwritten interop, unsafe product code, source dependencies, fake services,
shadow state, or a second scheduler/renderer/transport to bridge the gap.

Product rules and composition over Engine services belong downstream. A small
adapter is not a duplicate merely because it wraps an API. Do not demand broad
Engine adoption unrelated to the changed behavior. Use
`rusty-engine-reuse-audit` when that skill is available.
