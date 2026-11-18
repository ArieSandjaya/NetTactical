import { h } from '../dom.js';
import { api } from '../api.js';
import { btn, notice } from '../forms.js';

const SHOWN = 300;   // rows drawn at once; searching narrows the list
const searchBox = (st, paint, label) => {
  const el = h('input', { type: 'search', class: 'search', placeholder: label, 'aria-label': label, value: st.q, style: { maxWidth: 'none', flex: 'none', width: '100%' } });
  el.addEventListener('input', () => { st.q = el.value; paint(); const box = st.root.querySelector('input[type=search]'); if (box) { box.focus(); box.setSelectionRange(st.q.length, st.q.length); } });
  return el;
};
const has = (q, ...vals) => !q.trim() || vals.some((v) => (v || '').toLowerCase().includes(q.trim().toLowerCase()));
const why = (e) => (e.status === 403 ? 'You do not have permission to run commands on this asset.' : e.message);

// Windows services of one asset, read live from the agent each time (a list can take a few seconds). Created once per page.
export function servicesPanel(agentId) {
  const st = { rows: null, err: null, q: '', busy: false, acting: null, notice: null };
  const base = `agents/${encodeURIComponent(agentId)}/services`;
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } }); st.root = root;

  async function load() {
    st.busy = true; st.err = null; paint();
    try { st.rows = await api(base); } catch (e) { st.err = why(e); }
    st.busy = false; paint();
  }
  async function act(s, action) {
    const word = { start: 'Start', stop: 'Stop', restart: 'Restart' }[action];
    if (!confirm(`${word} the service "${s.display_name || s.name}"?${action !== 'start' ? '\n\nProgrammes that use it may stop working while it is down.' : ''}`)) return;
    st.acting = `${s.name}:${action}`; st.notice = null; paint();
    try { await api(`${base}/action`, { method: 'POST', body: { name: s.name, action } }); st.notice = { ok: true, msg: `${word} sent to ${s.display_name || s.name}. Reading the list again…` }; st.acting = null; paint(); st.rows = await api(base); }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.acting = null; paint();
  }

  function paint() {
    const run = (s) => /^run/i.test(s.status || '');
    const found = (st.rows || []).filter((s) => has(st.q, s.name, s.display_name, s.status));
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap', justifyContent: 'space-between' } },
        h('span', { class: 'muted' }, st.rows ? `${st.rows.length} services, read just now from the agent` : 'Asking the agent…'),
        btn(st.busy ? 'Asking the agent…' : 'Refresh', load, { disabled: st.busy })),
      notice(st.notice),
      st.err ? h('div', { class: 'banner', role: 'alert' }, st.err) : null,
      st.rows?.length ? searchBox(st, paint, 'Search services') : null,
      st.rows?.length ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, ['Service', 'Status', 'Start type', ''].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, found.slice(0, SHOWN).map((s) => h('tr', null,
          h('td', null, h('div', null, s.display_name || s.name), s.display_name ? h('div', { class: 'muted mono', style: { fontSize: '12px' } }, s.name) : null),
          h('td', null, h('span', { class: `pill ${run(s) ? 'good' : 'neutral'}` }, s.status || '?')),
          h('td', null, s.start_type || ''),
          h('td', { style: { textAlign: 'right', whiteSpace: 'nowrap' } },
            run(s) ? [btn(st.acting === `${s.name}:restart` ? 'Restarting…' : 'Restart', () => act(s, 'restart'), { disabled: !!st.acting }), ' ', btn(st.acting === `${s.name}:stop` ? 'Stopping…' : 'Stop', () => act(s, 'stop'), { danger: true, disabled: !!st.acting })]
              : btn(st.acting === `${s.name}:start` ? 'Starting…' : 'Start', () => act(s, 'start'), { disabled: !!st.acting })))),
        found.length ? null : h('caption', { class: 'empty' }, 'No service matches.')))) : null,
      found.length > SHOWN ? h('span', { class: 'muted' }, `Showing the first ${SHOWN} of ${found.length}. Search to narrow the list.`) : null,
    ].flat().filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint(); load();
  return { el: root };
}

const mb = (b) => (b == null ? '' : `${(b / 1048576).toFixed(b >= 104857600 ? 0 : 1)} MB`);

// Running processes of one asset, read live; a process can be ended after a confirmation.
export function processesPanel(agentId) {
  const st = { rows: null, err: null, q: '', sort: 'memory', busy: false, killing: null, notice: null };
  const base = `agents/${encodeURIComponent(agentId)}/processes`;
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } }); st.root = root;

  async function load() {
    st.busy = true; st.err = null; paint();
    try { st.rows = await api(base); } catch (e) { st.err = why(e); }
    st.busy = false; paint();
  }
  async function kill(p) {
    if (!confirm(`End "${p.name}" (process ${p.pid})?\n\nUnsaved work in it is lost.`)) return;
    st.killing = p.pid; st.notice = null; paint();
    try { await api(`${base}/kill`, { method: 'POST', body: { pid: p.pid, name: p.name } }); st.notice = { ok: true, msg: `${p.name} (${p.pid}) was ended.` }; st.killing = null; paint(); st.rows = await api(base); }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.killing = null; paint();
  }

  function paint() {
    const found = (st.rows || []).filter((p) => has(st.q, p.name, p.username, String(p.pid)));
    found.sort(st.sort === 'memory' ? (a, b) => (b.mem_bytes || 0) - (a.mem_bytes || 0) : st.sort === 'cpu' ? (a, b) => (b.cpu || 0) - (a.cpu || 0) : (a, b) => a.name.localeCompare(b.name));
    const sorter = h('select', { 'aria-label': 'Sort processes', onchange: (e) => { st.sort = e.target.value; paint(); } },
      [['memory', 'Most memory'], ['cpu', 'Most CPU'], ['name', 'Name']].map(([v, t]) => h('option', { value: v, selected: st.sort === v }, t)));
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap', justifyContent: 'space-between' } },
        h('span', { class: 'muted' }, st.rows ? `${st.rows.length} processes, read just now from the agent` : 'Asking the agent…'),
        h('div', { style: { display: 'flex', gap: '8px', alignItems: 'center' } }, st.rows?.length ? sorter : null, btn(st.busy ? 'Asking the agent…' : 'Refresh', load, { disabled: st.busy }))),
      notice(st.notice),
      st.err ? h('div', { class: 'banner', role: 'alert' }, st.err) : null,
      st.rows?.length ? searchBox(st, paint, 'Search name, user or process id') : null,
      st.rows?.length ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, ['Process', 'PID', 'Memory', 'CPU', 'User', ''].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, found.slice(0, SHOWN).map((p) => h('tr', null,
          h('td', null, p.name), h('td', { class: 'mono' }, String(p.pid)), h('td', null, mb(p.mem_bytes)), h('td', null, p.cpu == null ? '' : `${p.cpu.toFixed(1)} %`), h('td', null, p.username || ''),
          h('td', { style: { textAlign: 'right' } }, p.pid > 4 ? btn(st.killing === p.pid ? 'Ending…' : 'End', () => kill(p), { danger: true, disabled: st.killing != null }) : null))),
        found.length ? null : h('caption', { class: 'empty' }, 'No process matches.')))) : null,
      found.length > SHOWN ? h('span', { class: 'muted' }, `Showing the first ${SHOWN} of ${found.length}. Search to narrow the list.`) : null,
    ].flat().filter(Boolean));
  }

  paint(); load();
  return { el: root };
}
