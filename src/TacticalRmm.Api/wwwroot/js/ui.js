// Small shared controls for list pages.
import { h } from './dom.js';

export function chip(label, n, on, fn) {
  return h('button', { class: 'chip', 'aria-pressed': String(on), onclick: fn }, label, n == null ? null : h('span', { class: 'n' }, String(n)));
}

export function seg(items, current, fn, label) {
  return h('div', { class: 'seg', role: 'group', 'aria-label': label },
    items.map(([k, text]) => h('button', { 'aria-pressed': String(k === current), onclick: () => fn(k) }, text)));
}

export function searchBox(value, onInput, label) {
  const el = h('input', { type: 'search', class: 'search', placeholder: label, 'aria-label': label, value });
  el.addEventListener('input', () => onInput(el.value));
  return el;
}

// Generic loader: runs fetcher(signal), shows loading/error, repaints on success; polls when intervalMs is set.
export function loader(body, fetcher, paint, intervalMs = 0, loadingText = 'Loading…') {
  let alive = true, ctrl = null, timer = null, have = false;
  async function load(silent) {
    ctrl?.abort(); ctrl = new AbortController();
    if (!silent && !have) body.replaceChildren(h('div', { class: 'loading' }, loadingText));
    try {
      const data = await fetcher(ctrl.signal);
      if (!alive) return;
      have = true; paint(data, null);
    } catch (e) {
      if (e.name === 'AbortError' || !alive) return;
      if (!have) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, e.message));
      else paint(undefined, e.message);
    }
  }
  load(false);
  if (intervalMs) timer = setInterval(() => load(true), intervalMs);
  return { reload: () => load(true), stop: () => { alive = false; clearInterval(timer); ctrl?.abort(); } };
}
