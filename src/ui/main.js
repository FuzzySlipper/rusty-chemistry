/**
 * DOM-only sandbox panel. C# owns the field, the scenes, the tools and every choice; this module shows the
 * projection it publishes and claims `sandbox.command` intents when a button is pressed. Sections a scene
 * has no choices for are hidden.
 */
const COMMAND = 'sandbox.command';
const CONTRACT = 'rusty.chemistry.command.v1';

const STYLE = `
.chem-panel { position: absolute; top: 12px; left: 12px; width: 300px; max-height: calc(100% - 24px); overflow-y: auto;
  padding: 10px 12px; background: rgba(20, 22, 26, 0.84); color: #e8e6e1; border-radius: 8px;
  font: 13px/1.35 system-ui, sans-serif; }
.chem-panel h1 { font-size: 15px; margin: 0 0 6px; }
.chem-panel h2 { font-size: 11px; margin: 10px 0 4px; text-transform: uppercase; letter-spacing: 0.06em; color: #a9a59c; }
.chem-panel [hidden] { display: none !important; }
.chem-row { display: flex; flex-wrap: wrap; gap: 4px; }
.chem-row button { font: inherit; padding: 3px 8px; border-radius: 5px; border: 1px solid #555;
  background: #2c2f35; color: inherit; cursor: pointer; text-align: left; }
.chem-row button[aria-pressed="true"] { background: #c8692e; border-color: #e08a4c; color: #fff; }
.chem-row button small { display: block; font-size: 11px; opacity: 0.75; }
.chem-row button[data-vivid="true"] { box-shadow: 0 0 0 1px #ffe6a8, 0 0 8px 1px rgba(255, 214, 120, 0.7); }
.chem-row button[data-vivid="false"] { opacity: 0.72; }
.chem-stats, .chem-symptoms { font-variant-numeric: tabular-nums; color: #cfcbc2; }
.chem-stats div, .chem-symptoms div { display: flex; justify-content: space-between; gap: 8px; }
.chem-symptoms div span:last-child { text-align: right; }
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
    <div class="chem-row" data-group="scene"></div>
    <section data-section="tool"><h2>Tool</h2><div class="chem-row" data-group="tool"></div></section>
    <section data-section="emperor"><h2>Emperor</h2><div class="chem-row" data-group="emperor"></div></section>
    <section data-section="muted"><h2>Muted means</h2><div class="chem-row" data-group="muted"></div></section>
    <section data-section="symptoms"><h2>Symptoms</h2><div class="chem-symptoms" data-symptoms></div></section>
    <section data-section="overlay"><h2>Overlay</h2><div class="chem-row" data-group="overlay"></div></section>
    <section data-section="wind"><h2>Wind</h2><div class="chem-row" data-group="wind"></div></section>
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

  const group = (name) => panel.querySelector(`[data-group="${name}"]`);
  const section = (name) => panel.querySelector(`[data-section="${name}"]`);
  const run = panel.querySelector('[data-command="run"]');
  const stats = panel.querySelector('[data-stats]');
  const symptoms = panel.querySelector('[data-symptoms]');
  const last = panel.querySelector('[data-last]');

  const choices = (name, items, selected) => {
    const container = group(name);
    const key = JSON.stringify(items.map((item) => [item.id, item.label, item.detail]));
    if (container.dataset.key !== key) {
      container.dataset.key = key;
      container.replaceChildren(...items.map((item) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.choice = item.id;
        button.textContent = item.label;
        if (item.detail !== undefined) {
          const detail = document.createElement('small');
          detail.textContent = item.detail;
          button.append(detail);
        }
        return button;
      }));
    }
    items.forEach((item, index) => {
      const button = container.children[index];
      button.setAttribute('aria-pressed', String(item.id === selected));
      if (item.vivid === undefined) {
        delete button.dataset.vivid;
      } else {
        button.dataset.vivid = String(item.vivid);
      }
    });
    const holder = section(name);
    if (holder !== null) {
      holder.hidden = items.length === 0;
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
    choices('scene', view.scenes, view.scene);
    choices('tool', view.tools, view.tool);
    choices('emperor', view.emperors, view.emperor);
    choices('muted', view.muteds, view.muted);
    choices('overlay', [...view.overlays, { id: 'none', label: 'Off' }], view.overlay);
    choices('wind', view.winds, view.wind);
    run.textContent = view.running ? 'Pause' : 'Run';

    section('symptoms').hidden = view.symptoms.length === 0;
    symptoms.replaceChildren(...view.symptoms.map((symptom) =>
      row(symptom.name, `${symptom.medium} ${symptom.level.toFixed(2)} · ${symptom.jammed}/${symptom.gears} jammed`)));

    const count = (material) => view.counts.find((entry) => entry.material === material)?.cells;
    const burning = view.counts.filter((entry) => entry.material.startsWith('burning-')).reduce((sum, entry) => sum + entry.cells, 0);
    stats.replaceChildren(
      row('Step', view.step),
      row('Active chunks', `${view.activeChunks} / ${view.chunks}`),
      row('Step time', `${view.stepMs.toFixed(2)} ms`),
      ...(count('ash') === undefined ? [] : [row('Burning · ash', `${burning} · ${count('ash')}`)]),
      row('Fronts · casts pending', `${view.fronts} · ${view.pending}`),
      row('Overlay cubes', view.overlayCubes),
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
