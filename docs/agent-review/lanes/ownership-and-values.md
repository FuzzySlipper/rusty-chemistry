# Lane: Ownership and values

Use for cross-owner changes, definitions, or tuning. Does each decision and
value live with the owner that gives it meaning?

Quote the assumption/value at file/line, name its current and intended owners,
and show which uses become wrong when it changes. C# owns product policy and
state; Engine owns its named mechanisms; DOM code owns presentation and semantic
actions. Separate authored definitions, runtime state, and transient projections.

Adjustable gameplay values belong with typed domain configuration when tuning
is needed. Structural constants stay near the algorithm. Do not demand a file
for every literal, move vocabulary without moving authority, or introduce a
factory/interface merely for stylistic consistency.
