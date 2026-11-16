import { h } from './dom.js';
import { api } from './api.js';
import { notice, btn } from './forms.js';

// Editable custom field values of one client, site or agent. The element is created once and reused, so a repaint of the page never loses what was typed.
// It stays hidden when no field is defined for the model, or when the user may not see the values.
export function customFieldsPanel(model, target, { title = 'Custom fields' } = {}) {
  const el = h('section', { class: 'card stack', style: { gap: '12px', display: 'none' } });
  const st = { rows: null, orig: {}, draft: {}, notice: null, busy: false };

  async function load() {
    try {
      st.rows = await api(`custom-fields/values/${model}/${encodeURIComponent(target)}`);
      st.orig = Object.fromEntries(st.rows.map((r) => [r.field_id, r.value]));
      st.draft = { ...st.orig };
    } catch { st.rows = null; }
    paint();
  }

  async function save() {
    const values = {};
    for (const [id, v] of Object.entries(st.draft)) if (v !== st.orig[id]) values[id] = v;
    if (!Object.keys(values).length) { st.notice = { ok: true, msg: 'Nothing changed.' }; return paint(); }
    st.busy = true; st.notice = null; paint();
    try {
      st.rows = await api(`custom-fields/values/${model}/${encodeURIComponent(target)}`, { method: 'PUT', body: { values } });
      st.orig = Object.fromEntries(st.rows.map((r) => [r.field_id, r.value]));
      st.draft = { ...st.orig };
      st.notice = { ok: true, msg: 'Saved.' };
    } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  function control(r) {
    const set = (v) => { st.draft[r.field_id] = v; };
    const label = `${r.name}`;
    if (r.type === 'checkbox' || r.type === 'single') {
      const opts = r.type === 'checkbox' ? [['', 'Not set'], ['true', 'Yes'], ['false', 'No']] : [['', 'Not set'], ...(r.options || '').split('\n').filter(Boolean).map((o) => [o, o])];
      const sel = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none', width: '100%' }, 'aria-label': label },
        opts.map(([v, t]) => h('option', { value: v, selected: (st.draft[r.field_id] ?? '') === v }, t)));
      sel.addEventListener('change', () => set(sel.value));
      return sel;
    }
    const type = r.type === 'number' ? 'number' : r.type === 'datetime' ? 'datetime-local' : 'text';
    const inp = h('input', { type, class: 'search', style: { maxWidth: 'none', flex: 'none', width: '100%' }, 'aria-label': label, autocomplete: 'off',
      step: r.type === 'number' ? 'any' : null, maxlength: r.type === 'text' ? '2000' : null, value: st.draft[r.field_id] ?? '' });
    inp.addEventListener('input', () => set(inp.value));
    return inp;
  }

  function paint() {
    if (!st.rows || st.rows.length === 0) { el.style.display = 'none'; el.replaceChildren(); return; }
    el.style.display = '';
    el.replaceChildren(...[
      h('h2', { style: { fontSize: '16px' } }, title),
      h('div', { style: { display: 'grid', gap: '12px', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' } },
        st.rows.map((r) => h('label', { class: 'stack', style: { gap: '4px' } }, h('span', { class: 'eyebrow' }, r.name), control(r),
          r.default_value && !(st.draft[r.field_id] ?? '') ? h('span', { class: 'muted', style: { fontSize: '12px' } }, `Default: ${r.default_value === 'true' ? 'Yes' : r.default_value === 'false' ? 'No' : r.default_value}`) : null))),
      notice(st.notice),
      h('div', null, btn('Save', save, { primary: true, disabled: st.busy }))].filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  load();
  return { el, reload: load };
}
