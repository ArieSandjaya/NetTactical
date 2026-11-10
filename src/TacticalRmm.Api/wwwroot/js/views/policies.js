import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, check, notice, btn } from '../forms.js';
import { scope } from '../scope.js';

export function policiesView(root) {
  const st = { rows: null, notice: null, busy: false, edit: null };   // edit: null | {id|null, name, desc, active, enforced}
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('automation/policies', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading policies…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('automation/policies'); st.edit = null; st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const save = () => {
    const e = st.edit, p = { name: e.name.trim(), desc: e.desc.trim() || null, active: e.active, enforced: e.enforced };
    if (!p.name) { st.notice = { ok: false, msg: 'Name is required.' }; return paint(); }
    run(() => (e.id ? api(`automation/policies/${e.id}`, { method: 'PUT', body: p }) : api('automation/policies', { method: 'POST', body: p })), e.id ? 'Policy saved.' : 'Policy created.');
  };
  const open = (kind, p) => { scope.kind = 'policy'; scope.id = String(p.id); location.hash = `#/${kind}`; };

  function form() {
    const e = st.edit;
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, e.id ? 'Edit policy' : 'New policy'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Name', input('text', e, 'name', 'e.g. Workstation baseline')), field('Description', input('text', e, 'desc', 'Optional'), '2 1 280px')),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, check('Active', e, 'active'), check('Enforced (overrides checks set directly on an agent)', e, 'enforced')),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Save', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.edit = null; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.rows) return;
    body.replaceChildren(...[
      pageHead('Policies', `${st.rows.length} policies`, btn('New policy', () => { st.edit = { id: null, name: '', desc: '', active: true, enforced: false }; st.notice = null; paint(); }, { primary: true, disabled: !!st.edit })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.edit ? form() : null,
      h('section', { class: 'card' }, gridTable('cols-policy', ['Policy', 'Status', 'Agents', 'Checks', 'Tasks', ''], st.rows.map((p) => h('div', { class: 'tr cols-policy' },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '600' } }, p.name), p.desc ? h('span', { class: 'muted' }, p.desc) : null),
        h('div', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap' } }, h('span', { class: `pill ${p.active ? 'good' : 'neutral'}` }, p.active ? 'Active' : 'Inactive'), p.enforced ? h('span', { class: 'pill info' }, 'Enforced') : null),
        h('div', { class: 'mono' }, String(p.agent_count)), h('div', { class: 'mono' }, String(p.check_count)), h('div', { class: 'mono' }, String(p.task_count)),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          btn('Checks', () => open('checks', p)), btn('Tasks', () => open('tasks', p)),
          btn('Edit', () => { st.edit = { id: p.id, name: p.name, desc: p.desc || '', active: p.active, enforced: p.enforced }; st.notice = null; paint(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete policy "${p.name}" with its ${p.check_count} checks and ${p.task_count} tasks?`)) run(() => api(`automation/policies/${p.id}`, { method: 'DELETE' }), 'Policy deleted.'); }, { disabled: st.busy, danger: true })))),
        'No policies yet.'),
        h('p', { class: 'muted', style: { marginTop: '12px' } }, 'Assign a policy to a client, site or agent to apply its checks and tasks. Use Checks and Tasks to edit what a policy contains.'))].filter(Boolean));
  }
  return ld.stop;
}
