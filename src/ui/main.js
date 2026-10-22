/**
 * DOM-only product UI. Engine owns the canvas, input delivery, and projection
 * transport; this module owns only the button and its local label.
 */
export function mountProductUi(root, context) {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Rusty Template counter');

  const title = document.createElement('h1');
  title.textContent = 'Rusty Template';
  panel.append(title);

  const increment = document.createElement('button');
  increment.type = 'button';
  increment.textContent = 'Increment';
  increment.dataset.rustyTemplateIncrement = 'true';
  panel.append(increment);

  const value = document.createElement('output');
  value.id = 'rusty-template-counter';
  value.textContent = '0';
  value.setAttribute('aria-live', 'polite');
  panel.append(value);
  root.append(panel);

  const onIncrement = () => {
    context?.intents?.claim?.('increment', { kind: 'digital', active: true });
  };
  increment.addEventListener('click', onIncrement);

  let unsubscribe;
  const projection = context?.projection;
  if (projection?.subscribe !== undefined) {
    unsubscribe = projection.subscribe((envelope) => {
      const nextValue = envelope?.value?.value;
      if (typeof nextValue === 'number' && Number.isFinite(nextValue)) {
        value.textContent = String(nextValue);
      }
    });
  }

  return Object.freeze({
    dispose: () => {
      increment.removeEventListener('click', onIncrement);
      unsubscribe?.();
      panel.remove();
    },
  });
}
