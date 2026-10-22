# Agent review workflow

These packets are a starting point for any downstream Rusty Engine product.
Review the requested change and its real callers. Findings are claims for the
implementer to reconcile, not instructions to broaden the task or request
another user approval.

## Select lanes

The two reuse questions apply to every change. A documentation-only or trivial
change can record that no mechanism is affected; it does not require a roster
of agents. For substantial implementation, use bounded independent reviewers
when the active task or review workflow calls for them.

| Lane | When to use |
| --- | --- |
| [Engine reuse](lanes/engine-reuse.md) | Always check: existing upstream mechanisms and missing safe capabilities |
| [Existing product reuse](lanes/existing-product-reuse.md) | Always check: local ownership and competing implementations |
| [Ownership and values](lanes/ownership-and-values.md) | Cross-owner changes, authored definitions, or tuning |
| [Behavior and interoperability](lanes/behavior-and-interoperability.md) | Changed behavior, callers, UI, content, or save contracts |
| [Requirement and acceptance](lanes/requirement-and-acceptance.md) | Explicit task acceptance clauses |
| [Error and boundary paths](lanes/error-and-boundary-paths.md) | Changed input admission, parsing, resource lifetime, or failure paths |
| [Runtime trust](lanes/runtime-trust.md) | Added validation, guards, snapshots, or recovery machinery |
| [Test claims](lanes/test-claims.md) | New tests or claims that checks establish behavior |

Choose distinct questions. Do not run every lane by default, demand a fixed
reviewer count, or turn optional interactive testing into a universal gate.
Engine reuse asks whether the mechanism belongs upstream; product reuse asks
whether this repository already has its owner. Keep those findings distinct.

## Reviewer packet

Give each reviewer this guide and its selected lane, plus:

- Repository path, exact commit/diff or working-tree artifact, and owned scope.
- Original user/task requirements, exclusions, and directly applicable contracts.
- Changed paths and relevant callers or state owners.
- Installed Engine package/runtime identity for boundary changes.
- Checks already run, their results, and known limitations.

Use the collaboration tools actually available in the current harness. Do not
copy another tool's lifecycle or assume reviewers can see the conversation.
Fresh-context reviewers need the full packet. Reviewers stay read-only unless
explicitly assigned a fix; preserve concurrent edits and adjacent repositories.

## Findings and revision rounds

Report `no findings`, `findings`, or `unable to verify`. For each finding give:

1. A stable ID and the task-owned requirement or boundary it concerns.
2. Exact file/line or a focused reproducing command and observed result.
3. The concrete consequence for the requested behavior.
4. The minimum property needed for closure and a focused verification.

Separate a verified defect from an upstream gap, an unanswered question, or an
optional improvement. Do not block on style, missing paperwork, hypothetical
callers, broad coverage, or a preferred redesign. Passing compilation is not
proof of visible interaction; missing browser evidence is not a defect when
the task only calls for compilation.

Send fixes back to the same reviewer, naming finding IDs, changed paths, checks,
and any declined or deferred findings with reasons. Use the harness's mechanism
that resumes an idle reviewer when necessary. Recheck the original finding;
do not start an unrelated review on every round. A fresh reviewer is useful
only when the scope changed materially or independence needs to be restored.

The lead resolves disagreements against evidence and user/task scope. Record
each substantive finding as fixed, declined with a reason, or deferred to a
named receiving task when authorized. Reviewers cannot amend acceptance criteria.

## Customize for a new repository

Keep these questions generic. Add product owner pointers to the architecture
map and relevant lane, and link genuine donor/provenance or task-system policy
only when it exists. Set a product-specific trust boundary if the product adds
untrusted inputs or multiplayer requirements. Add review triggers only for
concrete risks. Do not copy another product's campaigns, temporary migration
rules, task IDs, fixed reviewer roster, or game vocabulary.
