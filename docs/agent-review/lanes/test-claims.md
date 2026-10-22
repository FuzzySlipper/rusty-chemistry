# Lane: Test claims

Use for changed tests or verification claims. Do the checks establish the
behavior claimed, and would a meaningful regression fail them?

Name the claim, the check/assertion at file/line or command, and the concrete
regression it misses. Watch for skipped bodies, swallowed errors, unreachable
fixtures, and assertions that merely restate implementation branches.

Keep compilation, SDK staging, NativeAOT publishing, host startup, and visible
interaction evidence distinct. None silently proves the others. Require new
checks only for a specific task-owned uncertainty; general coverage, a new
framework, or broad browser certification is not this lane's purpose.
