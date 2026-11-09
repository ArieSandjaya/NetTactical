import { h } from '../dom.js';
import { api } from '../api.js';
import { ago, fmtDate, sevClass, sevWord } from '../format.js';
import { seg, loader } from '../ui.js';

export function alertsView(root) {
  const st = { resolved: false, sev: 'all', data: null, busy: new Set(), notice: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let ld = null;

  function start() {
    ld?.stop(); st.data = null;
    ld = loader(body, (signal) => api(`alerts?resolved=${st.resolved}`, { signal }), (d, err) => { if (d) st.data = d; paint(err); }, 60000, 'Loading alerts…');
  }

  async function resolve(a) {
    st.busy.add(a.id); paint();
    try { await api(`alerts/${a.id}/resolve`, { method: 'PUT' }); st.data = st.data.filter((x) => x.id !== a.id); st.notice = null; }
    catch (e) { st.notice = `Could not resolve the alert: ${e.message}`; }
    st.busy.delete(a.id); paint();
  }

  function paint(err) {
    if (!st.data) return;
    const rows = st.data.filter((a) => st.sev === 'all' || a.severity === st.sev);
    const n = (s) => st.data.filter((a) => a.severity === s).length;
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      st.notice ? h('div', { class: 'banner', role: 'alert' }, st.notice) : null,
      h('div', { class: 'page-head' },
        h('div', { class: 'stack' }, h('h1', null, 'Alerts'),
          h('div', { class: 'muted' }, st.resolved ? `${st.data.length} resolved alerts (latest 500)` : `${st.data.length} open · ${n('error')} critical · ${n('warning')} warnings`)),
        h('div', { class: 'tabs-inline' },
          seg([[false, 'Open'], [true, 'Resolved']], st.resolved, (k) => { st.resolved = k; st.notice = null; start(); }, 'State'),
          seg([['all', 'All'], ['error', 'Critical'], ['warning', 'Warning'], ['info', 'Info']], st.sev, (k) => { st.sev = k; paint(); }, 'Severity'))),
      h('div', { class: 'card' }, h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
        h('div', { class: 'tr th cols-alert' }, h('div', null, 'Severity'), h('div', null, 'Alert'), h('div', null, 'Asset'),
          h('div', null, st.resolved ? 'Resolved' : 'Raised'), h('div', null, '')),
        rows.map((a) => h('div', { class: 'tr cols-alert' },
          h('div', null, h('span', { class: `pill ${sevClass(a.severity)}` }, sevWord(a.severity))),
          h('div', { class: 'stack', style: { gap: '2px' } }, h('span', null, a.message || 'Alert'), h('span', { class: 'muted', style: { fontSize: '12px' } }, a.alert_type || '')),
          h('div', { class: 'stack', style: { gap: '2px' } },
            a.agent_id ? h('a', { class: 'mono', href: `#/device/${encodeURIComponent(a.agent_id)}` }, a.hostname) : h('span', { class: 'muted' }, '—'),
            h('span', { class: 'muted', style: { fontSize: '12px' } }, a.client ? `${a.client} · ${a.site}` : '')),
          h('div', { class: 'muted', style: { fontSize: '12px' }, title: fmtDate(st.resolved ? a.resolved_on : a.alert_time) }, ago(st.resolved ? a.resolved_on : a.alert_time)),
          h('div', { style: { textAlign: 'right' } }, st.resolved ? null
            : h('button', { class: 'btn', disabled: st.busy.has(a.id), onclick: () => resolve(a) }, st.busy.has(a.id) ? 'Resolving…' : 'Resolve')))),
        rows.length === 0 ? h('div', { class: 'empty' }, st.resolved ? 'No resolved alerts.' : 'No open alerts.') : null)))].filter(Boolean));
  }

  start();
  return () => ld?.stop();
}
