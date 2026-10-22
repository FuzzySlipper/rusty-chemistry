# DOM companion

`src/ui/main.js` exports `mountProductUi`. It claims the declared
`sandbox.command` intent with a `rusty.chemistry.command.v1` payload
(`{command, id?}`: `scene`, `tool`, `emperor`, `muted`, `overlay`, `wind`, `run`,
`step`, `reset`) and
observes the Engine-delivered sandbox projection. The product project
selects this directory and module for SDK staging.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility, and semantic actions.
Sandbox state lives in C#; input delivery, projection transport, the canvas,
and rendering belong to Engine. Dispose event listeners and subscriptions
when the host unmounts the UI.
