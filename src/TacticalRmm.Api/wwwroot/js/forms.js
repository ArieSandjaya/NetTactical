// Small form helpers shared by the management pages. Values live in a plain object so a repaint never loses what was typed.
import { h } from './dom.js';

export const field = (label, el, grow = '1 1 180px') => h('label', { class: 'stack', style: { gap: '4px', flex: grow } }, h('span', { class: 'eyebrow' }, label), el);

export function input(type, obj, key, placeholder = '', extra = {}) {
  const el = h('input', { type, class: 'search', style: { maxWidth: 'none', flex: 'none' }, placeholder, autocomplete: 'off', value: obj[key] ?? '', ...extra });
  el.addEventListener('input', () => { obj[key] = el.value; });
  return el;
}

export function check(label, obj, key, onChange, disabled = false) {
  const el = h('input', { type: 'checkbox', disabled });
  el.checked = !!obj[key];
  el.addEventListener('change', () => { obj[key] = el.checked; onChange?.(); });
  return h('label', { class: 'check' }, el, h('span', null, label));
}

export function select(options, obj, key) {
  const el = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none' } },
    options.map(([v, text]) => h('option', { value: String(v), selected: String(obj[key] ?? '') === String(v) }, text)));
  el.addEventListener('change', () => { obj[key] = el.value; });
  return el;
}

export const notice = (n) => (n ? h('div', { class: n.ok ? 'pill good' : 'banner', role: n.ok ? 'status' : 'alert' }, n.msg) : null);
export const btn = (label, fn, { primary = false, disabled = false, danger = false } = {}) =>
  h('button', { class: `btn${primary ? ' primary' : ''}${danger ? ' danger' : ''}`, disabled, onclick: fn }, label);
