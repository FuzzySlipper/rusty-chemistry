# Lane: Runtime trust

Use when validation, guards, snapshots, compatibility, or recovery machinery is
added. Does it prevent a concrete failure at this boundary?

Name the machinery at file/line, why the path is trusted or untrusted, the
claimed failure and its reachable caller, and whether the existing owner
already establishes the invariant. If redundant, identify the simpler direct
operation or appropriate admission boundary.

Ordinary first-party state and Engine-admitted content should not acquire
repeated hashing, proposal/acceptance, revision policing, whole-state rollback,
or historical save readers without an explicit need. Reads should use live
owners, not invoke save capture just to inspect a field.

Keep genuine eligibility, changed-target rechecks, current-save integrity,
resource lifetime/disposal, and clear errors for malformed current data.
Offline import, installation, external input, explicit previews, or a stated
network trust boundary can justify different checks. Do not remove a safeguard
merely because it validates something; identify its actual consequence first.
