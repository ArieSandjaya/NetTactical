import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, btn, notice } from '../forms.js';

const RISK = { ok: ['good', 'No problems found'], watch: ['warn', 'Watch'], high: ['crit', 'High risk'], unknown: ['neutral', 'Not checked yet'] };
const why = (e) => (e.status === 403 ? 'You do not have permission for hardware checks.' : e.message);
const when = (iso) => { const d = iso ? new Date(iso) : null; return d && !Number.isNaN(d.getTime()) ? d.toLocaleString() : ''; };
const gb = (b) => (b ? `${Math.round(b / 1e9)} GB` : '');
const pill = (risk) => { const [c, t] = RISK[risk] || RISK.unknown; return h('span', { class: `pill ${c}` }, t); };
const LEVEL = { high: 'crit', watch: 'warn' };

// Hardware of one asset: serial, disk health, battery, a risk with its reasons, and the warranty date. Created once per page.
export function hardwarePanel(agentId, plat) {
  const st = { data: null, err: null, busy: false, form: { end: '', note: '' }, notice: null, loaded: false };
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });
  const base = `agents/${encodeURIComponent(agentId)}/hardware`;

  async function load() {
    try { st.data = await api(base); st.form = { end: st.data.warranty_end || '', note: st.data.warranty_note || '' }; st.err = null; } catch (e) { st.err = why(e); }
    st.loaded = true; paint();
  }
  async function refresh() {
    st.busy = true; st.notice = null; paint();
    try { st.data = await api(`${base}/refresh`, { method: 'POST' }); st.notice = { ok: true, msg: 'Checked just now.' }; } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; paint();
  }
  async function saveWarranty() {
    st.notice = null;
    try { st.data = await api(`${base}/warranty`, { method: 'PUT', body: { end_date: st.form.end, note: st.form.note } }); st.notice = { ok: true, msg: 'Warranty saved.' }; } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    paint();
  }

  function paint() {
    if (!st.loaded) { root.replaceChildren(h('span', { class: 'muted' }, 'Loading…')); return; }
    if (st.err) { root.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    const d = st.data; const s = d.snapshot;
    const windows = /^windows$/i.test(plat || '');
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } }, pill(d.risk),
        d.collected_at ? h('span', { class: 'muted' }, `Last check ${when(d.collected_at)}`) : null,
        windows ? btn(st.busy ? 'Checking…' : 'Check now', refresh, { primary: true, disabled: st.busy }) : h('span', { class: 'muted' }, 'The hardware check is only available for Windows assets.'),
        notice(st.notice)),
      d.reasons.length ? h('ul', { class: 'stack', style: { gap: '6px', paddingLeft: '18px', margin: 0 } }, d.reasons.map((r) => h('li', null, h('span', { class: `pill ${LEVEL[r.level] || 'neutral'}`, style: { marginRight: '8px' } }, r.level === 'high' ? 'High' : 'Watch'), r.text))) : null,
      s ? h('div', { class: 'facts' }, [['Maker', s.manufacturer], ['Model', s.model], ['Serial number', s.serial], ['BIOS', [s.bios_version, s.bios_date].filter(Boolean).join(' · ')]]
        .filter(([, v]) => v).map(([k, v]) => h('div', null, h('span', { class: 'k' }, k), h('span', { class: 'v' }, v)))) : null,
      s?.disks?.length ? h('table', { class: 'tbl' }, h('caption', { class: 'eyebrow', style: { textAlign: 'left' } }, 'Disks'),
        h('thead', null, h('tr', null, ['Disk', 'Type', 'Size', 'Health', 'Wear', 'Temp', 'Errors', 'Power-on'].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, s.disks.map((k) => h('tr', null, h('td', null, k.name || ''), h('td', null, k.media || ''), h('td', null, gb(k.size_bytes)), h('td', null, [k.health, k.status && k.status !== 'OK' ? k.status : ''].filter(Boolean).join(' · ')),
          h('td', null, k.wear_percent == null ? '–' : `${k.wear_percent}%`), h('td', null, k.temperature_c ? `${k.temperature_c} °C` : '–'),
          h('td', null, k.read_errors == null && k.write_errors == null ? '–' : String((k.read_errors || 0) + (k.write_errors || 0))), h('td', null, k.power_on_hours == null ? '–' : `${Math.round(k.power_on_hours / 24)} d`))))) : null,
      s?.battery && s.battery.design_mwh ? h('div', { class: 'muted' }, `Battery: ${Math.round((100 * (s.battery.full_mwh || 0)) / s.battery.design_mwh)}% of original capacity${s.battery.cycles != null ? ` · ${s.battery.cycles} charge cycles` : ''}`) : null,
      h('section', { class: 'card stack', style: { gap: '10px' } },
        h('strong', null, 'Warranty'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
          field('Ends on', (() => { const i = input('text', st.form, 'end', 'yyyy-MM-dd'); i.setAttribute('maxlength', '10'); return i; })(), '0 1 160px'),
          field('Note (optional)', input('text', st.form, 'note', 'for example: 3-year on-site'), '1 1 220px'),
          btn('Save', saveWarranty)),
        h('div', { class: 'muted' }, 'Enter the date from the maker\'s warranty page. The server does not look it up for you, and a date here is warned about 60 days before it ends.'),
        d.vendor_link ? h('a', { href: d.vendor_link, target: '_blank', rel: 'noopener noreferrer' }, `Open the maker's warranty page${s?.serial ? ` (serial ${s.serial})` : ''}`) : null),
    ].filter(Boolean));
  }

  load();
  return { el: root };
}

// Fleet list: every checked asset, worst first.
export function hardwareView(root) {
  const st = { list: null, only: 'attention' };
  const body = h('div', { class: 'stack', style: { gap: '16px' } });
  root.replaceChildren(body);
  let alive = true;
  async function load() {
    try { st.list = await api('hardware'); if (alive) paint(); }
    catch (e) { if (alive) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, why(e))); }
  }
  function paint() {
    const rows = st.only === 'attention' ? st.list.filter((r) => r.risk === 'high' || r.risk === 'watch') : st.list;
    const sel = h('select', { class: 'search', style: { maxWidth: '220px', flex: 'none' }, 'aria-label': 'Which assets' },
      [['attention', 'Needing attention'], ['all', 'All checked assets']].map(([v, t]) => h('option', { value: v, selected: st.only === v }, t)));
    sel.addEventListener('change', () => { st.only = sel.value; paint(); });
    body.replaceChildren(
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Hardware'),
        h('p', { class: 'muted' }, 'Disk health, battery wear and warranty of Windows assets. Online assets are checked about once a day. To check many now, tick them in Assets and choose "Hardware check".'))),
      sel,
      rows.length ? h('table', { class: 'tbl' },
        h('thead', null, h('tr', null, ['Risk', 'Asset', 'Client / site', 'Model', 'Why', 'Warranty'].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, rows.map((r) => h('tr', null, h('td', null, pill(r.risk)),
          h('td', null, h('a', { href: `#/device/${encodeURIComponent(r.agent_id)}` }, r.hostname)), h('td', null, `${r.client} / ${r.site}`), h('td', null, [r.manufacturer, r.model].filter(Boolean).join(' ')),
          h('td', null, r.reasons.map((x) => x.text).join(' ')), h('td', null, r.warranty_end || '–')))))
        : h('div', { class: 'empty' }, st.list.length ? 'Nothing needs attention.' : 'No asset has been checked yet.'));
  }
  load();
  return () => { alive = false; };
}
