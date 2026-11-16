import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, select, notice, btn } from '../forms.js';

const MODELS = [['client', 'Client'], ['site', 'Site'], ['agent', 'Asset']];
const TYPES = [['text', 'Text'], ['number', 'Number'], ['checkbox', 'Yes / No'], ['datetime', 'Date and time'], ['single', 'Pick one']];
const label = (list, v) => list.find(([k]) => k === v)?.[1] ?? v;

// Fields an administrator defines once; the values are filled in on the Clients & Sites page and on each asset's page.
export function customFieldsView(root) {
  const st = { rows: null, notice: null, busy: false, edit: null };   // edit: null | {id|null, name, model, type, options, default_value}
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('custom-fields', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading custom fields…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('custom-fields'); st.edit = null; st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const save = () => {
    const e = st.edit;
    if (!e.name.trim()) { st.notice = { ok: false, msg: 'Name is required.' }; return paint(); }
    const p = { name: e.name.trim(), options: e.type === 'single' ? e.options : null, default_value: e.default_value || '' };
    if (e.id) run(() => api(`custom-fields/${e.id}`, { method: 'PUT', body: p }), 'Field saved.');
    else run(() => api('custom-fields', { method: 'POST', body: { ...p, model: e.model, type: e.type } }), 'Field created.');
  };
  const blank = () => ({ id: null, name: '', model: 'agent', type: 'text', options: '', default_value: '' });

  function form() {
    const e = st.edit;
    const fixed = (text) => h('div', { class: 'search', style: { maxWidth: 'none', flex: 'none', display: 'flex', alignItems: 'center' } }, text);
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, e.id ? 'Edit custom field' : 'New custom field'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } },
        field('Name', input('text', e, 'name', 'e.g. Contract number')),
        field('Belongs to', e.id ? fixed(label(MODELS, e.model)) : select(MODELS, e, 'model'), '0 1 160px'),
        field('Type', e.id ? fixed(label(TYPES, e.type)) : (() => { const s = select(TYPES, e, 'type'); s.addEventListener('change', () => paint()); return s; })(), '0 1 180px')),
      e.type === 'single' ? field('Choices (one per line)', (() => { const t = h('textarea', { class: 'search', rows: '4', style: { maxWidth: 'none', flex: 'none', height: 'auto' }, 'aria-label': 'Choices' }); t.value = e.options || ''; t.addEventListener('input', () => { e.options = t.value; }); return t; })()) : null,
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Default value (optional)', input('text', e, 'default_value', e.type === 'checkbox' ? 'true or false' : e.type === 'datetime' ? 'e.g. 2026-12-31T09:00' : ''))),
      h('span', { class: 'muted', style: { fontSize: '12px' } }, e.id ? 'The model and type cannot be changed after creation. Removing a choice clears that value where it was used.' : 'The model and type cannot be changed after creation.'),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Save', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.edit = null; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.rows) return;
    body.replaceChildren(...[
      pageHead('Custom fields', `${st.rows.length} fields`, btn('New field', () => { st.edit = blank(); st.notice = null; paint(); }, { primary: true, disabled: !!st.edit })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.edit ? form() : null,
      h('section', { class: 'card' }, gridTable('cols-alerttpl', ['Field', 'Belongs to', 'Type', 'Filled in', ''], st.rows.map((f) => h('div', { class: 'tr cols-alerttpl' },
        h('span', { style: { fontWeight: '600' } }, f.name),
        h('div', null, label(MODELS, f.model)),
        h('div', null, label(TYPES, f.type)),
        h('div', { class: 'muted' }, `${f.value_count} value${f.value_count === 1 ? '' : 's'}`),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          btn('Edit', () => { st.edit = { id: f.id, name: f.name, model: f.model, type: f.type, options: f.options || '', default_value: f.default_value || '' }; st.notice = null; paint(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete custom field "${f.name}"? Its ${f.value_count} value(s) are deleted too.`)) run(() => api(`custom-fields/${f.id}`, { method: 'DELETE' }), 'Field deleted.'); }, { disabled: st.busy, danger: true })))),
        'No custom fields yet.'),
        h('p', { class: 'muted', style: { marginTop: '12px' } }, 'Values are filled in on the Clients & Sites page (clients and sites) and on each asset\'s page. A field\'s default value shows wherever no value is set.'))].filter(Boolean));
  }
  return ld.stop;
}
