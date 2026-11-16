import { h } from '../dom.js';
import { api, download } from '../api.js';
import { loader, pageHead, seg } from '../ui.js';
import { notice, btn } from '../forms.js';

const PREVIEW = 200;   // rows shown on screen; the CSV file has all of them

// Reports: pick one, narrow it to a client or site (and a period for alerts), look at it, download it as CSV.
export function reportsView(root) {
  const st = { reports: null, clients: [], name: null, client: '', site: '', from: '', to: '', table: null, busy: false, notice: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, async (signal) => ({ reports: await api('reports', { signal }), clients: await api('clients', { signal }).catch(() => []) }),
    (d, err) => { if (d) { st.reports = d.reports; st.clients = d.clients; if (!st.name || !st.reports.some((r) => r.name === st.name)) st.name = st.reports[0]?.name ?? null; } paint(err); }, 0, 'Loading reports…');

  const info = () => st.reports.find((r) => r.name === st.name);
  const query = () => {
    const q = new URLSearchParams();
    if (st.site) q.set('site_id', st.site); else if (st.client) q.set('client_id', st.client);
    if (info()?.uses_period) { if (st.from) q.set('from', st.from); if (st.to) q.set('to', st.to); }
    const s = q.toString();
    return s ? `?${s}` : '';
  };

  async function run() {
    st.busy = true; st.notice = null; paint();
    try { st.table = await api(`reports/${st.name}${query()}`); } catch (e) { st.table = null; st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  async function csv() {
    st.busy = true; st.notice = null; paint();
    try {
      const { blob, filename } = await download(`reports/${st.name}/csv${query()}`, { method: 'GET' });
      const url = URL.createObjectURL(blob);
      const link = h('a', { href: url, download: filename });
      document.body.append(link); link.click(); link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 10000);
      st.notice = { ok: true, msg: `${filename} downloaded.` };
    } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  const sel = (label, value, options, fn) => {
    const s = h('select', { class: 'search', 'aria-label': label, style: { maxWidth: 'none', flex: 'none', width: '100%' } },
      options.map(([v, t]) => h('option', { value: v, selected: String(v) === String(value) }, t)));
    s.addEventListener('change', () => fn(s.value));
    return h('label', { class: 'stack', style: { gap: '4px', flex: '1 1 200px' } }, h('span', { class: 'eyebrow' }, label), s);
  };
  const date = (label, value, fn) => {
    const i = h('input', { type: 'date', class: 'search', 'aria-label': label, value, style: { maxWidth: 'none', flex: 'none', width: '100%' } });
    i.addEventListener('input', () => fn(i.value));
    return h('label', { class: 'stack', style: { gap: '4px', flex: '1 1 160px' } }, h('span', { class: 'eyebrow' }, label), i);
  };

  function preview() {
    const t = st.table;
    if (!t) return null;
    const shown = t.rows.slice(0, PREVIEW);
    return h('section', { class: 'card stack', style: { gap: '10px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' } },
        h('h2', { style: { fontSize: '16px' } }, t.title), h('span', { class: 'muted' }, `${t.scope} · ${t.rows.length} row${t.rows.length === 1 ? '' : 's'}`)),
      t.truncated ? h('div', { class: 'banner', role: 'status' }, 'There are more alerts in this period than the report holds (5000). Narrow the period or the client to see the rest.') : null,
      t.rows.length > PREVIEW ? h('span', { class: 'muted' }, `Showing the first ${PREVIEW} rows. The CSV file has all ${t.rows.length}.`) : null,
      h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, t.columns.map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, shown.map((r) => h('tr', null, r.map((v) => h('td', null, v))))),
        shown.length ? null : h('caption', { class: 'empty' }, 'No rows for these filters.'))));
  }

  function paint(err) {
    if (!st.reports) return;
    if (!st.reports.length) { body.replaceChildren(pageHead('Reports', null), h('section', { class: 'card' }, h('p', { class: 'muted' }, 'Your role does not allow any report.'))); return; }
    const sites = st.clients.find((c) => String(c.id) === String(st.client))?.sites ?? [];
    body.replaceChildren(...[
      pageHead('Reports', 'Look at a report here, or download it as a CSV file for a spreadsheet.'),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      h('section', { class: 'card stack', style: { gap: '14px' } },
        seg(st.reports.map((r) => [r.name, r.title]), st.name, (k) => { st.name = k; st.table = null; st.notice = null; paint(); }, 'Report'),
        h('p', { class: 'muted' }, info().description),
        h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } },
          sel('Client', st.client, [['', 'All clients'], ...st.clients.map((c) => [c.id, c.name])], (v) => { st.client = v; st.site = ''; paint(); }),
          sel('Site', st.site, [['', 'All sites'], ...sites.map((s) => [s.id, s.name])], (v) => { st.site = v; paint(); }),
          info().uses_period ? date('From', st.from, (v) => { st.from = v; }) : null,
          info().uses_period ? date('To', st.to, (v) => { st.to = v; }) : null),
        info().uses_period ? h('span', { class: 'muted', style: { fontSize: '12px' } }, 'Leave the dates empty for the last 30 days. The period can be at most one year. Times are in UTC.') : null,
        h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap' } }, btn('Show report', run, { primary: true, disabled: st.busy }), btn('Download CSV', csv, { disabled: st.busy })),
        notice(st.notice)),
      preview()].filter(Boolean));
  }
  return ld.stop;
}
