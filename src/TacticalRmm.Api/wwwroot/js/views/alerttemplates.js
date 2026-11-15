import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, check, notice, btn } from '../forms.js';

// Simple alert templates: "always notify for ..." flags that apply to every agent of a client or site (or everyone, for the default template).
export function alertTemplatesView(root) {
  const st = { rows: null, notice: null, busy: false, edit: null };   // edit: null | {id|null, name, enabled, is_default, flag_agent_overdue, flag_check_failures, flag_task_failures}
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('alert-templates', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading alert templates…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('alert-templates'); st.edit = null; st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const save = () => {
    const e = st.edit;
    const p = { name: e.name.trim(), enabled: e.enabled, is_default: e.is_default, flag_agent_overdue: e.flag_agent_overdue, flag_check_failures: e.flag_check_failures, flag_task_failures: e.flag_task_failures };
    if (!p.name) { st.notice = { ok: false, msg: 'Name is required.' }; return paint(); }
    run(() => (e.id ? api(`alert-templates/${e.id}`, { method: 'PUT', body: p }) : api('alert-templates', { method: 'POST', body: p })), e.id ? 'Template saved.' : 'Template created.');
  };
  const blank = () => ({ id: null, name: '', enabled: true, is_default: false, flag_agent_overdue: false, flag_check_failures: false, flag_task_failures: false });
  const flagsText = (t) => [t.flag_agent_overdue && 'Offline agents', t.flag_check_failures && 'Failing checks', t.flag_task_failures && 'Failing tasks'].filter(Boolean).join(', ') || 'Nothing';

  function form() {
    const e = st.edit;
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, e.id ? 'Edit alert template' : 'New alert template'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Name', input('text', e, 'name', 'e.g. Always notify for servers'))),
      h('div', { class: 'stack', style: { gap: '6px' } }, h('span', { class: 'eyebrow' }, 'Always notify for'),
        h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } },
          check('Offline agents', e, 'flag_agent_overdue'), check('Failing checks', e, 'flag_check_failures'), check('Failing tasks', e, 'flag_task_failures')),
        h('span', { class: 'muted', style: { fontSize: '12px' } }, 'These alerts count as flagged, so notification channels set to "only flagged" send them. A template never removes a flag a check, task or agent already has.')),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, check('Enabled', e, 'enabled'), check('Default (applies where the client and site have no template)', e, 'is_default')),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Save', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.edit = null; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.rows) return;
    body.replaceChildren(...[
      pageHead('Alert templates', `${st.rows.length} templates`, btn('New template', () => { st.edit = blank(); st.notice = null; paint(); }, { primary: true, disabled: !!st.edit })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.edit ? form() : null,
      h('section', { class: 'card' }, gridTable('cols-alerttpl', ['Template', 'Status', 'Always notify for', 'Used by', ''], st.rows.map((t) => h('div', { class: 'tr cols-alerttpl' },
        h('span', { style: { fontWeight: '600' } }, t.name),
        h('div', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap' } }, h('span', { class: `pill ${t.enabled ? 'good' : 'neutral'}` }, t.enabled ? 'Enabled' : 'Disabled'), t.is_default ? h('span', { class: 'pill info' }, 'Default') : null),
        h('div', null, flagsText(t)),
        h('div', { class: 'muted' }, `${t.client_count} client${t.client_count === 1 ? '' : 's'}, ${t.site_count} site${t.site_count === 1 ? '' : 's'}`),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          btn('Edit', () => { st.edit = { ...t }; st.notice = null; paint(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete alert template "${t.name}"? Clients and sites that use it fall back to the next level.`)) run(() => api(`alert-templates/${t.id}`, { method: 'DELETE' }), 'Template deleted.'); }, { disabled: st.busy, danger: true })))),
        'No alert templates yet.'),
        h('p', { class: 'muted', style: { marginTop: '12px' } }, 'Assign a template to a client or site on the Clients & Sites page. The site\'s template wins over the client\'s, and the default template applies where neither has one.'))].filter(Boolean));
  }
  return ld.stop;
}
