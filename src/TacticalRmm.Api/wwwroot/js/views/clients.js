import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, input, notice, btn } from '../forms.js';
import { customFieldsPanel } from '../customfields.js';

export function clientsView(root) {
  const st = { clients: null, policies: null, templates: null, defs: [], open: new Set(), panels: new Map(), counts: new Map(), notice: null, busy: false, edit: null, newClient: { name: '', site: '' }, newSite: {} };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  async function fetchAll(signal) {
    const [clients, fleet, policies, templates, defs] = await Promise.all([api('clients', { signal }), api('fleet/assets', { signal }).catch(() => null),
      api('automation/policies', { signal }).catch(() => null), api('alert-templates', { signal }).catch(() => null), api('custom-fields', { signal }).catch(() => [])]);
    return { clients, fleet, policies, templates, defs };
  }
  function apply({ clients, fleet, policies, templates, defs }) {
    st.clients = clients;
    st.defs = defs || [];
    st.policies = policies;
    st.templates = templates;
    st.counts = new Map();
    for (const a of fleet?.assets || []) { const k = `${a.client}|${a.site}`; st.counts.set(k, (st.counts.get(k) || 0) + 1); }
  }
  const ld = loader(body, fetchAll, (d, err) => { if (d) apply(d); paint(err); }, 0, 'Loading clients…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); apply(await fetchAll()); st.edit = null; st.newClient = { name: '', site: '' }; st.newSite = {}; st.notice = { ok: true, msg: okMsg }; }
    catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  const assetsOf = (c) => c.sites.reduce((n, s) => n + (st.counts.get(`${c.name}|${s.name}`) || 0), 0);
  const unit = (n) => `${n} asset${n === 1 ? '' : 's'}`;

  // Policy for a whole client or site. Hidden when the user may not list policies.
  function policyPicker(kind, id, current) {
    if (!st.policies) return null;
    const el = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none', width: '100%', minWidth: '0' }, 'aria-label': 'Policy', disabled: st.busy },
      [h('option', { value: '', selected: current == null }, 'No policy'),
        ...st.policies.map((p) => h('option', { value: String(p.id), selected: p.id === current }, p.active ? p.name : `${p.name} (inactive)`))]);
    el.addEventListener('change', () => {
      const body = el.value === '' ? { clear_policy: true } : { policy: Number(el.value) };
      run(() => api(kind === 'client' ? `clients/${id}` : `sites/${id}`, { method: 'PUT', body }), 'Policy updated.');
    });
    return el;
  }

  // Alert template for a whole client or site. Hidden when the user may not list alert templates.
  function templatePicker(kind, id, current) {
    if (!st.templates) return null;
    const el = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none', width: '100%', minWidth: '0' }, 'aria-label': 'Alert template', disabled: st.busy },
      [h('option', { value: '', selected: current == null }, 'No template'),
        ...st.templates.map((t) => h('option', { value: String(t.id), selected: t.id === current }, t.enabled ? t.name : `${t.name} (disabled)`))]);
    el.addEventListener('change', () => {
      const body = el.value === '' ? { clear_alert_template: true } : { alert_template: Number(el.value) };
      run(() => api(kind === 'client' ? `clients/${id}` : `sites/${id}`, { method: 'PUT', body }), 'Alert template updated.');
    });
    return el;
  }

  // Custom field values of a client or site: a toggle button, and the panel under the card. Panels are kept so typed text survives a repaint.
  function fieldsToggle(kind, id) {
    if (!st.defs.some((f) => f.model === kind)) return null;
    const k = `${kind}:${id}`;
    return btn(st.open.has(k) ? 'Fields ▴' : 'Fields ▾', () => { if (st.open.has(k)) st.open.delete(k); else st.open.add(k); paint(); }, { disabled: st.busy });
  }
  function fieldsPanel(kind, id, title) {
    const k = `${kind}:${id}`;
    if (!st.open.has(k)) return null;
    if (!st.panels.has(k)) st.panels.set(k, customFieldsPanel(kind, String(id), { title }));
    return st.panels.get(k).el;
  }

  function nameCell(kind, id, name) {
    if (st.edit && st.edit.kind === kind && st.edit.id === id) {
      return h('div', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' } }, input('text', st.edit, 'name', 'Name'),
        btn('Save', () => run(() => api(kind === 'client' ? `clients/${id}` : `sites/${id}`, { method: 'PUT', body: { name: st.edit.name } }), 'Renamed.'), { primary: true, disabled: st.busy }),
        btn('Cancel', () => { st.edit = null; paint(); }));
    }
    return h('span', { style: { fontWeight: kind === 'client' ? '600' : '500', fontSize: kind === 'client' ? '16px' : '14px' } }, name);
  }

  function clientCard(c) {
    const n = assetsOf(c);
    const siteRows = c.sites.map((s) => h('div', { class: 'tr cols-site' },
      nameCell('site', s.id, s.name),
      h('div', { class: 'muted' }, unit(st.counts.get(`${c.name}|${s.name}`) || 0)),
      policyPicker('site', s.id, s.policy_id) || h('div'),
      templatePicker('site', s.id, s.alert_template_id) || h('div'),
      h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
        fieldsToggle('site', s.id),
        btn('Rename', () => { st.edit = { kind: 'site', id: s.id, name: s.name }; paint(); }, { disabled: st.busy }),
        btn('Delete', () => { if (confirm(`Delete site "${s.name}"? Sites that still have assets cannot be deleted.`)) run(() => api(`sites/${s.id}`, { method: 'DELETE' }), 'Site deleted.'); }, { disabled: st.busy }))));
    st.newSite[c.id] ??= { name: '' };
    return h('section', { class: 'card' },
      h('div', { class: 'page-head', style: { alignItems: 'center', padding: '4px 0 12px' } },
        h('div', { class: 'stack', style: { gap: '2px' } }, nameCell('client', c.id, c.name), h('span', { class: 'muted' }, `${c.sites.length} site${c.sites.length === 1 ? '' : 's'} · ${unit(n)}`)),
        h('div', { style: { display: 'flex', gap: '6px' } },
          fieldsToggle('client', c.id),
          btn('Rename', () => { st.edit = { kind: 'client', id: c.id, name: c.name }; paint(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete client "${c.name}" and all its sites? Clients that still have assets cannot be deleted.`)) run(() => api(`clients/${c.id}`, { method: 'DELETE' }), 'Client deleted.'); }, { disabled: st.busy }))),
      (st.policies || st.templates) ? h('div', { style: { display: 'flex', gap: '24px', flexWrap: 'wrap', padding: '0 0 16px' } },
        st.policies ? h('div', { class: 'stack', style: { gap: '4px', flex: '1 1 240px', maxWidth: '340px' } }, h('span', { class: 'eyebrow' }, 'Policy for all sites'), policyPicker('client', c.id, c.policy_id),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, 'A site or agent policy overrides this one.')) : null,
        st.templates ? h('div', { class: 'stack', style: { gap: '4px', flex: '1 1 240px', maxWidth: '340px' } }, h('span', { class: 'eyebrow' }, 'Alert template for all sites'), templatePicker('client', c.id, c.alert_template_id),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, 'A site template overrides this one.')) : null) : null,
      fieldsPanel('client', c.id, `Custom fields: ${c.name}`),
      h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' }, siteRows, c.sites.length === 0 ? h('div', { class: 'empty' }, 'No sites.') : null)),
      c.sites.map((s) => fieldsPanel('site', s.id, `Custom fields: ${s.name}`)),
      h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'flex-end', marginTop: '12px' } },
        field('Add site', input('text', st.newSite[c.id], 'name', 'Site name'), '1 1 220px'),
        btn('Add site', () => run(() => api('clients/sites', { method: 'POST', body: { name: st.newSite[c.id].name, client: c.id } }), 'Site added.'), { primary: true, disabled: st.busy })));
  }

  function paint(err) {
    if (!st.clients) return;
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      notice(st.notice),
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Clients & Sites'),
        h('div', { class: 'muted' }, `${st.clients.length} clients · ${st.clients.reduce((n, c) => n + c.sites.length, 0)} sites`))),
      st.clients.map(clientCard),
      st.clients.length === 0 ? h('div', { class: 'card empty' }, 'No clients yet.') : null,
      h('section', { class: 'card' }, h('h2', { style: { fontSize: '16px' } }, 'Add client'),
        h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'flex-end' } },
          field('Client name', input('text', st.newClient, 'name', 'e.g. Head Office')), field('First site', input('text', st.newClient, 'site', 'e.g. Main')),
          btn('Add client', () => run(() => api('clients', { method: 'POST', body: { name: st.newClient.name, site: st.newClient.site } }), 'Client added.'), { primary: true, disabled: st.busy })))].flat().filter(Boolean));
  }
  return ld.stop;
}
