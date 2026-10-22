# Lane: Existing product reuse

**Always check.** Does the change extend this repository's actual owner, or
introduce competing state or behavior?

Start with `AGENTS.md` and `docs/architecture.md`, then search the implementation
and callers. The template's counter domain owns its value; the product entry
owns lifecycle composition; the DOM only displays facts. Customize those owner
pointers as the product grows.

An actionable finding names the existing type/member, the new duplicate at
file/line, the overlapping authority, and the callers or invariant that now
can disagree. For misplaced behavior, show the concrete domain owner and why
putting the behavior elsewhere creates competing responsibility.

New files, similar names, or a direct method are not defects. Prefer explicit
composition and extending a real domain owner. Do not prescribe a universal
framework, registry, bus, or speculative abstraction as the replacement.
