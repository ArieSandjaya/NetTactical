import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, input, notice, btn } from '../forms.js';

export function clientsView(root) {
  const st = { clients: null, counts: new Map(), notice: null, busy: false, edit: null, newClient: { name: '', site: '' }, newSite: {} };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  async function fetchAll(signal) {
    const [clients, fleet] = await Promise.all([api('clients', { signal }), api('fleet/assets', { signal }).catch(() => null)]);
    return { clients, fleet };
  }
  function apply({ clients, fleet }) {
    st.clients = clients;
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
      h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end' } },
        btn('Rename', () => { st.edit = { kind: 'site', id: s.id, name: s.name }; paint(); }, { disabled: st.busy }),
        btn('Delete', () => { if (confirm(`Delete site "${s.name}"? Sites that still have assets cannot be deleted.`)) run(() => api(`sites/${s.id}`, { method: 'DELETE' }), 'Site deleted.'); }, { disabled: st.busy }))));
    st.newSite[c.id] ??= { name: '' };
    return h('section', { class: 'card' },
      h('div', { class: 'page-head', style: { alignItems: 'center', padding: '4px 0 12px' } },
        h('div', { class: 'stack', style: { gap: '2px' } }, nameCell('client', c.id, c.name), h('span', { class: 'muted' }, `${c.sites.length} site${c.sites.length === 1 ? '' : 's'} · ${unit(n)}`)),
        h('div', { style: { display: 'flex', gap: '6px' } },
          btn('Rename', () => { st.edit = { kind: 'client', id: c.id, name: c.name }; paint(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete client "${c.name}" and all its sites? Clients that still have assets cannot be deleted.`)) run(() => api(`clients/${c.id}`, { method: 'DELETE' }), 'Client deleted.'); }, { disabled: st.busy }))),
      h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' }, siteRows, c.sites.length === 0 ? h('div', { class: 'empty' }, 'No sites.') : null)),
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
