import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, notice, btn } from '../forms.js';

// Reusable pieces of script. Write {{name}} in a script and the snippet's code is put there each time the script runs.
export function snippetsView(root) {
  const st = { rows: null, notice: null, busy: false, edit: null };   // edit: null | {id|null, name, description, code}
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('script-snippets', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading snippets…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('script-snippets'); st.edit = null; st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  async function openEdit(s) {
    st.busy = true; st.notice = null; paint();
    try { const d = await api(`script-snippets/${s.id}`); st.edit = { id: d.id, name: d.name, description: d.description || '', code: d.code || '' }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const save = () => {
    const e = st.edit;
    if (!e.id && !/^[A-Za-z0-9_-]{1,64}$/.test(e.name.trim())) { st.notice = { ok: false, msg: 'Name must be 1-64 letters, digits, "-" or "_" (no spaces).' }; return paint(); }
    if (!e.code.trim()) { st.notice = { ok: false, msg: 'Code is required.' }; return paint(); }
    const p = { description: e.description.trim(), code: e.code };
    if (e.id) run(() => api(`script-snippets/${e.id}`, { method: 'PUT', body: p }), 'Snippet saved. Every script that uses it now runs the new code.');
    else run(() => api('script-snippets', { method: 'POST', body: { ...p, name: e.name.trim() } }), 'Snippet created.');
  };

  function form() {
    const e = st.edit;
    const ta = h('textarea', { class: 'search mono', rows: 12, spellcheck: 'false', style: { maxWidth: 'none', flex: 'none', width: '100%', resize: 'vertical' }, placeholder: 'Code to insert', 'aria-label': 'Code' });
    ta.value = e.code; ta.addEventListener('input', () => { e.code = ta.value; });
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, e.id ? `Edit snippet: ${e.name}` : 'New snippet'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } },
        e.id ? field('Name', h('div', { class: 'search mono', style: { maxWidth: 'none', flex: 'none', display: 'flex', alignItems: 'center' } }, `{{${e.name}}}`), '0 1 260px')
          : field('Name', input('text', e, 'name', 'e.g. log_helper'), '0 1 260px'),
        field('Description (optional)', input('text', e, 'description', 'What it does'))),
      field('Code', ta),
      h('span', { class: 'muted', style: { fontSize: '12px' } }, e.id ? 'A snippet cannot be renamed, because scripts refer to it by name.' : 'Use letters, digits, "-" and "_". Scripts then use it as {{name}}.'),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Save', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.edit = null; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.rows) return;
    body.replaceChildren(...[
      pageHead('Script snippets', `${st.rows.length} snippets`, btn('New snippet', () => { st.edit = { id: null, name: '', description: '', code: '' }; st.notice = null; paint(); }, { primary: true, disabled: !!st.edit })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.edit ? form() : null,
      h('section', { class: 'card' }, gridTable('cols-alerttpl', ['Snippet', 'Description', 'Used by', '', ''], st.rows.map((s) => h('div', { class: 'tr cols-alerttpl' },
        h('span', { class: 'mono', style: { fontWeight: '600' } }, `{{${s.name}}}`),
        h('div', { class: 'muted' }, s.description || ''),
        h('div', { class: 'muted' }, `${s.used_by} script${s.used_by === 1 ? '' : 's'}`),
        h('div'),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          btn('Edit', () => openEdit(s), { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete snippet "${s.name}"?`)) run(() => api(`script-snippets/${s.id}`, { method: 'DELETE' }), 'Snippet deleted.'); }, { disabled: st.busy, danger: true })))),
        'No snippets yet.'),
        h('p', { class: 'muted', style: { marginTop: '12px' } }, 'Put {{name}} in a script, check or task script and the snippet\'s code is inserted when it runs on an agent. Insertion happens once: a snippet that contains {{other}} does not pull in another snippet. Unknown names are left as written. A snippet that scripts still use cannot be deleted.'))].filter(Boolean));
  }
  return ld.stop;
}
