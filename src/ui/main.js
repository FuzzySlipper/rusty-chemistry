/**
 * DOM-only sandbox panel. C# owns the field, the tools and every choice; this module shows the
 * projection it publishes and claims `sandbox.command` intents when a button is pressed.
 */
const COMMAND = 'sandbox.command';
const CONTRACT = 'rusty.chemistry.command.v1';

const STYLE = `
.chem-panel { position: absolute; top: 12px; left: 12px; width: 260px; padding: 10px 12px;
  background: rgba(20, 22, 26, 0.82); color: #e8e6e1; border-radius: 8px;
  font: 13px/1.35 system-ui, sans-serif; }
.chem-panel h1 { font-size: 15px; margin: 0 0 6px; }
.chem-panel h2 { font-size: 11px; margin: 10px 0 4px; text-transform: uppercase; letter-spacing: 0.06em; color: #a9a59c; }
.chem-row { display: flex; flex-wrap: wrap; gap: 4px; }
.chem-row button { font: inherit; padding: 3px 8px; border-radius: 5px; border: 1px solid #555;
  background: #2c2f35; color: inherit; cursor: pointer; }
.chem-row button[aria-pressed="true"] { background: #c8692e; border-color: #e08a4c; color: #fff; }
.chem-stats { font-variant-numeric: tabular-nums; color: #cfcbc2; }
.chem-stats div { display: flex; justify-content: space-between; }
.chem-last { margin-top: 8px; color: #f0c99a; min-height: 2.7em; }
.chem-help { margin-top: 6px; color: #8f8b83; font-size: 11px; }
`;

export function mountProductUi(root, context) {
  const style = document.createElement('style');
  style.textContent = STYLE;

  const panel = document.createElement('aside');
  panel.className = 'chem-panel';
  panel.setAttribute('aria-label', 'Chemistry sandbox');
  panel.innerHTML = `
    <h1>Chemistry sandbox</h1>
    <h2>Tool</h2><div class="chem-row" data-group="tool"></div>
    <h2>Overlay</h2><div class="chem-row" data-group="overlay"></div>
    <h2>Wind</h2><div class="chem-row" data-group="wind"></div>
    <h2>Field</h2>
    <div class="chem-row">
      <button type="button" data-command="run">Pause</button>
      <button type="button" data-command="step">Step</button>
      <button type="button" data-command="reset">Reset</button>
    </div>
    <div class="chem-stats" data-stats></div>
    <div class="chem-last" data-last aria-live="polite"></div>
    <div class="chem-help">Click: use tool · 1–9: pick tool · right-drag: orbit · middle-drag / WASD: pan · Q/E: turn · wheel: zoom</div>
  `;
  root.append(style, panel);

  const send = (command, id) => {
    context?.intents?.claim?.(COMMAND, { kind: 'product-payload', contract: CONTRACT, data: id === undefined ? { command } : { command, id } });
    // Hand the keyboard back to the arena so WASD keeps working after a click here.
    context?.ui?.focusGameplay?.();
  };

  const onClick = (event) => {
    const button = event.target.closest('button');
    if (button === null || !panel.contains(button)) {
      return;
    }
    if (button.dataset.command !== undefined) {
      send(button.dataset.command);
    } else if (button.dataset.choice !== undefined) {
      send(button.closest('[data-group]').dataset.group, button.dataset.choice);
    }
  };
  panel.addEventListener('click', onClick);

  const groups = {
    tool: panel.querySelector('[data-group="tool"]'),
    overlay: panel.querySelector('[data-group="overlay"]'),
    wind: panel.querySelector('[data-group="wind"]'),
  };
  const run = panel.querySelector('[data-command="run"]');
  const stats = panel.querySelector('[data-stats]');
  const last = panel.querySelector('[data-last]');

  const choices = (group, items, selected) => {
    const key = JSON.stringify(items);
    if (group.dataset.key !== key) {
      group.dataset.key = key;
      group.replaceChildren(...items.map((item) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.choice = item.id;
        button.textContent = item.label;
        return button;
      }));
    }
    for (const button of group.children) {
      button.setAttribute('aria-pressed', String(button.dataset.choice === selected));
    }
  };

  const row = (label, value) => {
    const line = document.createElement('div');
    const name = document.createElement('span');
    const text = document.createElement('span');
    name.textContent = label;
    text.textContent = String(value);
    line.append(name, text);
    return line;
  };

  const show = (view) => {
    if (view === null || typeof view !== 'object' || !Array.isArray(view.tools)) {
      return;
    }
    choices(groups.tool, view.tools, view.tool);
    choices(groups.overlay, [...view.overlays, { id: 'none', label: 'Off' }], view.overlay);
    choices(groups.wind, view.winds.map((id) => ({ id, label: id[0].toUpperCase() + id.slice(1) })), view.wind);
    run.textContent = view.running ? 'Pause' : 'Run';
    const burning = view.counts.filter((count) => count.material.startsWith('burning-')).reduce((sum, count) => sum + count.cells, 0);
    stats.replaceChildren(
      row('Step', view.step),
      row('Active chunks', `${view.activeChunks} / ${view.chunks}`),
      row('Step time', `${view.stepMs.toFixed(2)} ms`),
      row('Burning cells', burning),
      row('Ash · charcoal', `${view.counts.find((c) => c.material === 'ash')?.cells ?? 0} · ${view.counts.find((c) => c.material === 'charcoal')?.cells ?? 0}`),
      row('Fronts · overlay cubes', `${view.fronts} · ${view.overlayCubes}`),
    );
    last.textContent = view.last;
  };

  let unsubscribe;
  const projection = context?.projection;
  if (projection?.subscribe !== undefined) {
    unsubscribe = projection.subscribe((envelope) => show(envelope?.value ?? null));
  }

  return Object.freeze({
    dispose: () => {
      panel.removeEventListener('click', onClick);
      unsubscribe?.();
      panel.remove();
      style.remove();
    },
  });
}
