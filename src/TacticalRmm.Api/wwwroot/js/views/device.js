import { h } from '../dom.js';
import { api } from '../api.js';
import { lineChart } from '../charts.js';
import {
  ago, fmtDate, levelClass, levelWord, sevClass, sevWord, statusClass, statusWord, syncWord, patchWord, patchClass,
  checkType, KIND_LABELS, HINTS, METRIC_NAMES,
} from '../format.js';

const TABS = [['issues', 'Masalah'], ['checks', 'Check'], ['tasks', 'Task'], ['patches', 'Patch'], ['system', 'Disk & layanan']];

export function deviceView(root, agentId) {
  const st = { data: null, tab: 'issues', issue: 0, metric: 'cpu', days: 7, series: null, seriesErr: null };
  let alive = true, ctrl = null, timer = null;
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const id = encodeURIComponent(agentId);

  async function load(silent) {
    if (!silent) body.replaceChildren(h('div', { class: 'loading' }, 'Memuat detail perangkat…'));
    try {
      st.data = await api(`agents/${id}/overview`);
      if (!alive) return;
      paint();
      loadSeries();
    } catch (e) {
      if (!alive) return;
      if (!st.data) body.replaceChildren(back(), h('div', { class: 'banner', role: 'alert' }, e.message));
    }
  }

  async function loadSeries() {
    ctrl?.abort(); ctrl = new AbortController();
    try {
      st.series = await api(`agents/${id}/metrics?metric=${st.metric}&days=${st.days}`, { signal: ctrl.signal });
      st.seriesErr = null;
    } catch (e) { if (e.name === 'AbortError') return; st.seriesErr = e.message; st.series = null; }
    if (alive) paint();
  }

  const back = () => h('a', { class: 'back', href: '#/' }, '← Kembali ke ringkasan');

  function paint() {
    const d = st.data; if (!d) return;
    const a = d.agent, hl = d.health, lvl = levelClass(hl.level);
    const counts = { issues: d.issues.length, checks: d.checks.length, tasks: d.tasks.length, patches: d.patches.length, system: d.disks.length + d.services.length };

    const facts = [
      ['Terakhir lapor', a.online ? 'baru saja' : `${ago(a.last_seen)} (${fmtDate(a.last_seen)})`], ['IP publik', a.public_ip || '—'],
      ['Pengguna masuk', a.logged_in_user || '—'], ['Versi agent', a.version || '—'], ['Uptime', a.uptime_days == null ? '—' : `${a.uptime_days} hari`],
      ['Memori', a.total_ram_gb ? `${a.total_ram_gb} GB` : '—'],
    ];

    body.replaceChildren(
      back(),
      h('section', { class: 'card', style: { flexDirection: 'row', flexWrap: 'wrap', gap: '24px', justifyContent: 'space-between', padding: '24px' } },
        h('div', { class: 'stack', style: { flex: '2 1 420px', gap: '10px' } },
          h('div', { style: { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '12px' } },
            h('h1', { class: 'mono', style: { fontSize: '28px', fontWeight: '500' } }, a.hostname),
            h('span', { class: `pill ${a.online ? 'good' : 'crit'}` }, a.online ? 'Online' : 'Offline'),
            a.maintenance_mode ? h('span', { class: 'pill info' }, 'Mode pemeliharaan') : null,
            a.needs_reboot ? h('span', { class: 'pill warn' }, 'Perlu reboot') : null),
          h('div', { class: 'muted' }, `${a.client} · ${a.site} · ${a.os || a.plat}`),
          h('div', { class: 'facts', style: { marginTop: '4px' } }, facts.map(([k, v]) => h('div', null, h('span', { class: 'k' }, k), h('span', { class: 'v' }, v))))),
        h('div', { class: 'stack', style: { flex: '1 1 260px', gap: '12px' } },
          h('div', { style: { display: 'flex', alignItems: 'center', gap: '14px' } },
            h('span', { class: `score ${lvl}`, style: { minWidth: '64px', fontSize: '24px', padding: '14px 0', borderRadius: '50%' } }, String(hl.score)),
            h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { class: 'eyebrow' }, 'Skor kesehatan'),
              h('span', { style: { color: `var(--${lvl})`, fontWeight: '600' } }, levelWord(hl.level)))),
          hl.factors.length
            ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '6px' } }, hl.factors.map((f) => h('span', { class: 'chipx', title: `−${f.penalty} poin` }, `${f.detail} (−${f.penalty})`)))
            : h('span', { class: 'muted' }, 'Tidak ada faktor yang menurunkan skor.'))),

      h('section', { class: 'row' },
        metricTile('CPU', d.metrics.cpu), metricTile('Memori', d.metrics.memory), metricTile('Disk terpenuh', d.metrics.disk_worst)),

      h('section', { class: 'card' },
        h('div', { class: 'page-head' }, h('h2', null, `Riwayat ${METRIC_NAMES[st.metric]}`),
          h('div', { class: 'tabs-inline' },
            seg(Object.entries(METRIC_NAMES), st.metric, (k) => { st.metric = k; st.series = null; loadSeries(); paint(); }, 'Metrik'),
            seg([[7, '7 hr'], [30, '30 hr'], [90, '90 hr']], st.days, (n) => { st.days = n; st.series = null; loadSeries(); paint(); }, 'Rentang'))),
        st.seriesErr ? h('div', { class: 'banner' }, st.seriesErr)
          : lineChart((st.series?.points || []).map((p) => ({ t: p.time, v: p.value })), { label: `Riwayat ${METRIC_NAMES[st.metric]}` })),

      h('section', { class: 'panel' },
        h('div', { class: 'tabbar', role: 'tablist' }, TABS.map(([k, label]) =>
          h('button', { role: 'tab', 'aria-selected': String(st.tab === k), onclick: () => { st.tab = k; paint(); } }, label,
            h('span', { class: `count${k === 'issues' && counts[k] ? ' bad' : ''}` }, String(counts[k]))))),
        st.tab === 'issues' ? issuesTab(d) : st.tab === 'checks' ? checksTab(d) : st.tab === 'tasks' ? tasksTab(d)
          : st.tab === 'patches' ? patchesTab(d) : systemTab(d)));
  }

  function seg(items, current, fn, label) {
    return h('div', { class: 'seg', role: 'group', 'aria-label': label }, items.map(([k, text]) => h('button', { 'aria-pressed': String(k === current), onclick: () => fn(k) }, text)));
  }

  function metricTile(label, v) {
    const cls = v == null ? 'neutral' : v >= 90 ? 'crit' : v >= 80 ? 'warn' : 'good';
    return h('div', { class: 'card kpi', style: { padding: '16px 20px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' } }, h('span', { class: 'eyebrow' }, label),
        h('span', { class: 'mono', style: { fontSize: '20px', fontWeight: '500' } }, v == null ? '—' : `${Math.round(v)}%`)),
      h('div', { class: 'bar thin' }, v == null ? null : h('i', { class: cls, style: { width: `${v}%` } })),
      h('div', { class: 'muted', style: { fontSize: '12px' } }, v == null ? 'Belum ada data terbaru' : v >= 90 ? 'Di atas ambang kritis' : v >= 80 ? 'Mendekati ambang' : 'Normal'));
  }

  function issuesTab(d) {
    if (!d.issues.length) return h('div', { class: 'pad empty' }, 'Tidak ada masalah terbuka di perangkat ini.');
    const i = Math.min(st.issue, d.issues.length - 1), sel = d.issues[i];
    return h('div', { class: 'split' },
      h('div', { class: 'list' }, d.issues.map((x, n) => h('button', { class: 'issue', 'aria-current': String(n === i), onclick: () => { st.issue = n; paint(); } },
        h('span', { class: `dot ${sevClass(x.severity)}` }),
        h('span', { class: 'stack', style: { gap: '4px' } }, h('span', { class: 't' }, x.title),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, `${x.source}${x.last_seen ? ` · terakhir ${ago(x.last_seen)}` : ''}${x.count > 1 ? ` · ${x.count}×` : ''}`))))),
      h('div', { class: 'detail' },
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center' } },
          h('span', { class: `pill ${sevClass(sel.severity)}` }, sevWord(sel.severity)), h('span', { class: 'muted' }, KIND_LABELS[sel.kind] || sel.kind)),
        h('h2', { style: { fontSize: '18px', lineHeight: '1.35' } }, sel.title),
        h('div', { class: 'facts' },
          fact('Pertama terlihat', fmtDate(sel.first_seen)), fact('Terakhir terlihat', fmtDate(sel.last_seen)),
          fact('Kejadian', `${sel.count}×`), fact('Kode keluar', sel.return_code ?? '—')),
        sel.output ? h('div', { class: 'stack' }, h('span', { class: 'eyebrow' }, 'Output'), h('pre', { class: 'out' }, sel.output))
          : h('div', { class: 'muted' }, 'Tidak ada output yang tersimpan untuk masalah ini.'),
        h('div', { class: 'advice' }, h('b', null, 'Saran tindakan'), h('div', null, HINTS[sel.kind] || HINTS.alert))));
  }
  const fact = (k, v) => h('div', null, h('span', { class: 'k' }, k), h('span', { class: 'v' }, String(v)));

  function table(cols, head, rows, empty) {
    return h('div', { class: 'pad scroll-x' }, h('div', { class: 'grid-table' },
      h('div', { class: `tr th ${cols}` }, head.map((t) => h('div', null, t))), rows, rows.length ? null : h('div', { class: 'empty' }, empty)));
  }
  const checksTab = (d) => table('cols-check', ['Check', 'Status', 'Terakhir jalan', 'Hasil terakhir'],
    d.checks.map((c) => h('div', { class: 'tr cols-check' },
      h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '500' } }, c.name),
        h('span', { class: 'muted', style: { fontSize: '12px' } }, `${checkType(c.type)} · tiap ${c.run_interval} dtk${c.managed_by_policy ? ' · dari policy' : ''}`)),
      h('div', null, h('span', { class: `pill ${statusClass(c.status)}` }, statusWord(c.status))),
      h('div', { class: 'mono' }, ago(c.last_run)), h('div', { style: { overflowWrap: 'anywhere' } }, c.info || '—'))), 'Belum ada check.');
  const tasksTab = (d) => table('cols-task', ['Task', 'Status', 'Terakhir jalan', 'Sinkron ke agent'],
    d.tasks.map((t) => h('div', { class: 'tr cols-task' },
      h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '500' } }, t.name),
        h('span', { class: 'muted', style: { fontSize: '12px' } }, `${t.type}${t.enabled ? '' : ' · nonaktif'}${t.managed_by_policy ? ' · dari policy' : ''}`)),
      h('div', null, h('span', { class: `pill ${statusClass(t.status)}` }, statusWord(t.status))),
      h('div', { class: 'mono' }, ago(t.last_run)), h('div', null, syncWord(t.sync_status)))), 'Belum ada task.');
  const patchesTab = (d) => table('cols-patch', ['KB', 'Update', 'Tingkat', 'Status'],
    d.patches.map((p) => h('div', { class: 'tr cols-patch' }, h('div', { class: 'mono' }, p.kb || '—'), h('div', null, p.title || '—'),
      h('div', null, p.severity || '—'), h('div', null, h('span', { class: `pill ${patchClass(p.status)}` }, patchWord(p.status))))), 'Tidak ada update tertunda atau gagal.');

  function systemTab(d) {
    return h('div', { class: 'pad row' },
      h('div', { class: 'stack', style: { flex: '1 1 300px', gap: '10px' } }, h('h2', null, 'Disk'),
        d.disks.map((k) => h('div', { class: 'stack', style: { gap: '5px' } },
          h('div', { style: { display: 'flex', justifyContent: 'space-between' } }, h('span', { class: 'mono' }, `${k.device} ${k.total || ''}`), h('span', { class: 'mono' }, `${Math.round(k.percent)}%`)),
          h('div', { class: 'bar' }, h('i', { class: k.percent >= 90 ? 'crit' : k.percent >= 80 ? 'warn' : 'good', style: { width: `${k.percent}%` } })))),
        d.disks.length ? null : h('div', { class: 'empty' }, 'Agent belum melaporkan data disk.')),
      h('div', { class: 'stack', style: { flex: '1 1 300px', gap: '10px' } }, h('h2', null, 'Layanan otomatis yang berhenti'),
        d.services.map((x) => h('div', { class: 'kv' }, h('span', { class: 'mono' }, x.display_name || x.name), h('span', { style: { color: 'var(--crit)' } }, x.status || 'berhenti'))),
        d.services.length ? null : h('div', { class: 'empty' }, 'Semua layanan otomatis berjalan.')));
  }

  load(false);
  timer = setInterval(() => load(true), 60000);
  return () => { alive = false; clearInterval(timer); ctrl?.abort(); };
}
