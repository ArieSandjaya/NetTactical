import { h } from '../dom.js';
import { api } from '../api.js';
import { patchClass, patchSevClass, patchWord } from '../format.js';
import { seg, loader } from '../ui.js';

export function patchesView(root) {
  const st = { tab: 'updates', status: 'all', data: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  const ld = loader(body, (signal) => api('fleet/patches', { signal }), (d, err) => { if (d) st.data = d; paint(err); }, 120000, 'Loading patch data…');

  const kpi = (label, big, note) => h('div', { class: 'card kpi' }, h('div', { class: 'eyebrow' }, label), h('div', { class: 'big' }, big), h('div', { class: 'muted' }, note));

  function updatesTable(d) {
    const rows = d.updates.filter((u) => st.status === 'all' || u.status === st.status);
    return h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
      h('div', { class: 'tr th cols-upd' }, h('div', null, 'KB'), h('div', null, 'Update'), h('div', null, 'Severity'), h('div', null, 'Status'), h('div', { style: { textAlign: 'right' } }, 'Assets')),
      rows.map((u) => h('div', { class: 'tr cols-upd' },
        h('div', { class: 'mono' }, u.kb || '—'), h('div', null, u.title || 'Untitled update'),
        h('div', null, h('span', { class: `pill ${patchSevClass(u.severity)}` }, u.severity || 'Unspecified')),
        h('div', null, h('span', { class: `pill ${patchClass(u.status)}` }, patchWord(u.status))),
        h('div', { class: 'mono', style: { textAlign: 'right' } }, String(u.devices)))),
      rows.length === 0 ? h('div', { class: 'empty' }, 'No outstanding updates.') : null));
  }

  function devicesTable(d) {
    return h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
      h('div', { class: 'tr th cols-pdev' }, h('div', null, 'Asset'), h('div', { style: { textAlign: 'right' } }, 'Pending'), h('div', { style: { textAlign: 'right' } }, 'Failed'), h('div', null, 'Reboot'), h('div', null, 'Status')),
      d.devices.map((v) => h('div', { class: 'tr cols-pdev' },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('a', { class: 'mono', href: `#/device/${encodeURIComponent(v.agent_id)}`, style: { fontWeight: '500' } }, v.hostname),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, v.client)),
        h('div', { class: 'mono', style: { textAlign: 'right' } }, String(v.pending)),
        h('div', { class: 'mono', style: { textAlign: 'right', color: v.failed ? 'var(--crit)' : '' } }, String(v.failed)),
        h('div', null, v.needs_reboot ? h('span', { class: 'pill warn' }, 'Needs reboot') : h('span', { class: 'muted' }, '—')),
        h('div', null, h('span', { class: `pill ${v.online ? 'good' : 'crit'}` }, v.online ? 'Online' : 'Offline')))),
      d.devices.length === 0 ? h('div', { class: 'empty' }, 'All assets are up to date.') : null));
  }

  function paint(err) {
    const d = st.data; if (!d) return;
    const t = d.totals;
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Patches'), h('div', { class: 'muted' }, `${d.scope.name} · outstanding Windows updates`))),
      h('section', { class: 'row', 'aria-label': 'Patch totals' },
        kpi('Pending updates', String(t.pending_updates), 'waiting to install'),
        kpi('Failed installs', h('span', { style: { color: t.failed_updates ? 'var(--crit)' : '' } }, String(t.failed_updates)), 'need attention'),
        kpi('Assets affected', String(t.devices_affected), 'with pending or failed updates'),
        kpi('Need a reboot', String(t.needs_reboot), 'assets')),
      h('div', { class: 'card' },
        h('div', { class: 'page-head', style: { alignItems: 'center' } },
          seg([['updates', 'By update'], ['devices', 'By asset']], st.tab, (k) => { st.tab = k; paint(); }, 'View'),
          st.tab === 'updates' ? seg([['all', 'All'], ['failed', 'Failed'], ['pending', 'Pending']], st.status, (k) => { st.status = k; paint(); }, 'Status') : null),
        st.tab === 'updates' ? updatesTable(d) : devicesTable(d))].filter(Boolean));
  }
  return ld.stop;
}
