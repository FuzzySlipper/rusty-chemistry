# Lane: Error and boundary paths

Use for changed parsing, admission, lifetimes, or failure paths. What actual
input or state can reach this boundary, and how does it fail?

Name the trigger, path at file/line, and concrete wrong result: swallowed error,
silent default, lost resource, wrong value, or incoherent partial operation.
Prefer a focused executable reproduction. Inspect missing required content,
meaningful boundary values, disposal/replacement, and Engine call failures only
where the changed behavior reaches them.

Engine service calls are not a transaction over the whole product. Do not
assume a callback failure undoes earlier native mutations. Require the task's
actual consistency property, not invented snapshot/rollback machinery.

Trusted admitted data does not need repeated hostile-input checks. Preserve
real native lifetime and product eligibility rules. Do not demand validation
for impossible states or treat a correctly surfaced upstream failure as a
local defect; report its owner and blocked behavior.
