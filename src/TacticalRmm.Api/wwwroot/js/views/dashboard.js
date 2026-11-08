import { h } from '../dom.js';
import { api } from '../api.js';
import { lineChart, ring } from '../charts.js';
import { ago, levelClass, levelWord, scoreClass, sevClass, METRIC_NAMES } from '../format.js';

const DAYS = [7, 30, 90];

export function dashboardView(root) {
  const st = { clientId: null, days: 30, metric: 'cpu', data: null, clients: [], error: null };
  let alive = true, timer = null, ctrl = null;
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  async function load(silent = false) {
    ctrl?.abort(); ctrl = new AbortController();
    if (!silent && !st.data) body.replaceChildren(h('div', { class: 'loading' }, 'Loading asset data…'));
    try {
      const q = new URLSearchParams({ days: st.days, risk_limit: 10 });
      if (st.clientId) q.set('client_id', st.clientId);
      const d = await api(`fleet/summary?${q}`, { signal: ctrl.signal });
      if (!alive) return;
      st.data = d; st.error = null;
      // The client chip list comes from the unfiltered summary; while filtered, the old list is kept.
      if (!st.clientId) st.clients = d.clients.map((c) => ({ id: c.id, name: c.name, devices: c.devices }));
      paint();
    } catch (e) {
      if (e.name === 'AbortError' || !alive) return;
      st.error = e.message;
      if (!st.data) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.error));
      else paint();
    }
  }

  const pick = (patch, refetch) => { Object.assign(st, patch); refetch ? load() : paint(); };

  function paint() {
    const d = st.data; if (!d) return;
    const t = d.totals, lvl = levelClass(t.level);
    const total = Math.max(t.devices, 1);

    const chips = h('div', { class: 'chips' },
      chip('All', st.clients.reduce((n, c) => n + c.devices, 0), st.clientId === null, () => pick({ clientId: null }, true)),
      st.clients.map((c) => chip(c.name, c.devices, st.clientId === c.id, () => pick({ clientId: c.id }, true))));

    const kpi = (label, big, note) => h('div', { class: 'card kpi' }, h('div', { class: 'eyebrow' }, label), h('div', { class: 'big' }, big), h('div', { class: 'muted' }, note));

    const trend = d.trends[st.metric] || [];
    const pts = trend.map((p) => ({ t: p.date, v: p.avg }));
    const now = pts.length ? Math.round(pts.at(-1).v) : null;
    const avg = pts.length ? Math.round(pts.reduce((a, p) => a + p.v, 0) / pts.length) : null;

    body.replaceChildren(...[
      st.error ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${st.error}`) : null,
      h('div', { class: 'page-head' },
        h('div', { class: 'stack' }, h('h1', null, 'Asset health'),
          h('div', { class: 'muted' }, `${d.scope.name} · ${t.devices} devices`)),
        chips),

      h('section', { class: 'row', 'aria-label': 'Overview' },
        h('div', { class: 'card kpi-score' }, ring(t.avg_health, lvl),
          h('div', { class: 'stack' }, h('div', { class: 'eyebrow' }, 'Health score'),
            h('div', { style: { fontSize: '18px', fontWeight: '600', color: `var(--${lvl})` } }, levelWord(t.level)),
            h('div', { class: 'muted' }, `${t.at_risk} devices scoring below 70 need attention.`))),
        kpi('Agents online', [String(t.online), h('small', null, ` / ${t.devices}`)], `${t.offline} offline`),
        kpi('Active alerts', String(d.alerts.error + d.alerts.warning + d.alerts.info),
          [h('b', { style: { color: 'var(--crit)' } }, `${d.alerts.error} critical`), ` · ${d.alerts.warning} warnings`]),
        kpi('Pending updates', String(d.patch.pending_devices), `devices · ${d.patch.needs_reboot} need a reboot`)),

      h('section', { class: 'row' },
        h('div', { class: 'card', style: { flex: '2 1 460px' } },
          h('div', { class: 'page-head' },
            h('div', { class: 'stack' }, h('h2', null, `${st.days}-day trend`),
              h('div', { class: 'muted' }, now === null ? 'No metric data yet' : `Average ${METRIC_NAMES[st.metric]} · now ${now}% · mean ${avg}%`)),
            h('div', { class: 'tabs-inline' },
              seg(Object.entries(METRIC_NAMES), st.metric, (k) => pick({ metric: k }, false), 'Metric'),
              seg(DAYS.map((n) => [n, `${n} d`]), st.days, (n) => pick({ days: n }, true), 'Range'))),
          lineChart(pts, { label: `${METRIC_NAMES[st.metric]} trend chart` })),
        clientCard(d)),

      h('section', { class: 'row' }, riskCard(d), alertsCard(d)),
      h('section', { class: 'row' }, patchCard(d, total), osCard(d, total), versionCard(d))].filter(Boolean));
  }

  function chip(label, n, on, fn) {
    return h('button', { class: 'chip', 'aria-pressed': String(on), onclick: fn }, label, h('span', { class: 'n' }, String(n)));
  }
  function seg(items, current, fn, label) {
    return h('div', { class: 'seg', role: 'group', 'aria-label': label },
      items.map(([k, text]) => h('button', { 'aria-pressed': String(k === current), onclick: () => fn(k) }, text)));
  }

  function clientCard(d) {
    return h('div', { class: 'card', style: { flex: '1 1 300px' } },
      h('div', { class: 'stack' }, h('h2', null, 'Score by client'), h('div', { class: 'muted' }, 'Click to filter the whole page')),
      h('div', { class: 'stack', style: { gap: '10px' } },
        d.clients.map((c) => h('button', { class: 'issue', style: { padding: '10px 12px', borderRadius: '8px', flexDirection: 'column', gap: '6px' },
          'aria-current': String(st.clientId === c.id), onclick: () => pick({ clientId: st.clientId === c.id ? null : c.id }, true) },
          h('div', { style: { display: 'flex', justifyContent: 'space-between', gap: '8px', width: '100%' } },
            h('span', { style: { fontWeight: '500' } }, c.name), h('span', { class: 'mono' }, String(c.avg_health))),
          h('div', { class: 'bar', style: { width: '100%' } }, h('i', { class: scoreClass(c.avg_health), style: { width: `${c.avg_health}%` } })),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, `${c.devices} devices · ${c.at_risk} at risk`))),
        d.clients.length === 0 ? h('div', { class: 'empty' }, 'No clients yet.') : null));
  }

  function riskCard(d) {
    return h('div', { class: 'card', style: { flex: '2 1 460px' } },
      h('div', { class: 'stack' }, h('h2', null, 'Devices at risk'), h('div', { class: 'muted' }, 'Sorted by lowest score, with the main drivers')),
      h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
        h('div', { class: 'tr th cols-risk' }, h('div', null, 'Device'), h('div', null, 'Score'), h('div', null, 'Drivers'), h('div', { style: { textAlign: 'right' } }, 'Uptime')),
        d.at_risk.map((r) => h('div', { class: 'tr cols-risk' },
          h('div', { class: 'stack', style: { gap: '2px' } },
            h('a', { class: 'mono', href: `#/device/${encodeURIComponent(r.agent_id)}`, style: { fontWeight: '500' } }, r.hostname),
            h('span', { class: 'muted', style: { fontSize: '12px' } }, `${r.client} · ${r.os || 'Unknown OS'}`)),
          h('div', null, h('span', { class: `score ${levelClass(r.level)}` }, String(r.score))),
          h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '6px' } }, r.reasons.map((x) => h('span', { class: 'chipx' }, x.detail))),
          h('div', { class: 'mono', style: { textAlign: 'right' } }, r.uptime_days == null ? '—' : `${r.uptime_days} days`))),
        d.at_risk.length === 0 ? h('div', { class: 'empty' }, 'No devices at risk.') : null)));
  }

  function alertsCard(d) {
    return h('div', { class: 'card', style: { flex: '1 1 300px' } },
      h('div', { class: 'stack' }, h('h2', null, 'Recent alerts'), h('div', { class: 'muted' }, 'From checks and scheduled tasks')),
      h('div', null,
        d.recent_alerts.slice(0, 6).map((a) => h('div', { class: 'list-item' }, h('span', { class: `dot ${sevClass(a.severity)}` }),
          h('div', { class: 'stack', style: { gap: '3px' } },
            h('span', null, h('a', { href: `#/device/${encodeURIComponent(a.agent_id)}`, class: 'mono' }, a.hostname), ': ', a.message || 'Alert'),
            h('span', { class: 'muted', style: { fontSize: '12px' } }, `${a.client} · ${ago(a.time)}`)))),
        d.recent_alerts.length === 0 ? h('div', { class: 'empty' }, 'No open alerts.') : null));
  }

  function patchCard(d, total) {
    const p = d.patch, w = (n) => ({ width: `${(n / total) * 100}%` });
    const row = (cls, text, n) => h('div', { class: 'kv', style: { border: '0', padding: '0' } },
      h('span', { style: { display: 'flex', alignItems: 'center', gap: '8px' } }, h('span', { class: `dot ${cls}`, style: { margin: '0', borderRadius: '3px' } }), text),
      h('span', { class: 'mono' }, String(n)));
    return h('div', { class: 'card', style: { flex: '1 1 300px' } }, h('h2', null, 'Patch status'),
      h('div', { class: 'bar', style: { height: '14px', display: 'flex' } },
        h('i', { class: 'good', style: w(p.up_to_date) }), h('i', { class: 'warn', style: w(p.few) }), h('i', { class: 'crit', style: w(p.many) })),
      h('div', { class: 'stack', style: { gap: '10px' } },
        row('good', 'Up to date', p.up_to_date), row('warn', '1–4 pending updates', p.few), row('crit', '5+ pending updates', p.many),
        h('div', { class: 'kv', style: { borderTop: '1px solid var(--line-soft)', borderBottom: '0' } }, h('span', null, 'Needs reboot'), h('span', { class: 'mono' }, String(p.needs_reboot)))));
  }

  function osCard(d, total) {
    return h('div', { class: 'card', style: { flex: '1 1 300px' } }, h('h2', null, 'Operating systems'),
      h('div', { class: 'stack', style: { gap: '12px' } },
        d.operating_systems.slice(0, 8).map((o) => h('div', { class: 'stack', style: { gap: '5px' } },
          h('div', { style: { display: 'flex', justifyContent: 'space-between', gap: '8px' } }, h('span', null, o.name), h('span', { class: 'mono' }, String(o.count))),
          h('div', { class: 'bar thin' }, h('i', { class: 'accent', style: { width: `${(o.count / total) * 100}%` } })))),
        d.operating_systems.length === 0 ? h('div', { class: 'empty' }, 'No data yet.') : null));
  }

  function versionCard(d) {
    const v = d.agent_versions;
    return h('div', { class: 'card', style: { flex: '1 1 300px' } }, h('h2', null, 'Agent version'),
      h('div', { class: 'kv' }, h('span', null, ['Latest ', h('span', { class: 'mono muted' }, v.latest)]), h('span', { class: 'mono' }, String(v.current))),
      h('div', { class: 'kv' }, h('span', null, 'Needs update'), h('span', { class: 'mono', style: { color: v.outdated ? 'var(--warn)' : '', fontWeight: '600' } }, String(v.outdated))));
  }

  load();
  timer = setInterval(() => load(true), 60000);
  return () => { alive = false; clearInterval(timer); ctrl?.abort(); };
}
