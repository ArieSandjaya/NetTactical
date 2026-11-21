import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, select, btn, notice } from '../forms.js';

const LEVEL = { Critical: 'crit', Error: 'crit', Warning: 'warn', Information: 'info', Verbose: 'neutral' };
const why = (e) => (e.status === 403 ? 'You do not have permission to view event logs.' : e.message);
const when = (iso) => { const d = iso ? new Date(iso) : null; return d && !Number.isNaN(d.getTime()) ? d.toLocaleString() : ''; };

// Windows event log of one asset, read live from the agent. Created once per page and reused, so the filter fields keep what was typed.
export function eventLogPanel(agentId) {
  const st = { log: 'System', level: 'all', hours: '24', max: '100', text: '', eventId: '', busy: false, err: null, data: null, open: null };
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });

  async function load() {
    const q = new URLSearchParams({ log: st.log, level: st.level, hours: String(Math.max(1, Math.min(720, parseInt(st.hours, 10) || 24))), max: String(Math.max(1, Math.min(500, parseInt(st.max, 10) || 100))) });
    if (st.text.trim()) q.set('text', st.text.trim());
    if (st.eventId.trim()) q.set('event_id', st.eventId.trim());
    st.busy = true; st.err = null; st.open = null; paint();
    try { st.data = await api(`agents/${encodeURIComponent(agentId)}/eventlog?${q}`); } catch (e) { st.err = why(e); st.data = null; }
    st.busy = false; paint();
  }

  function paint() {
    const d = st.data;
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
        field('Log', select([['System', 'System'], ['Application', 'Application'], ['Security', 'Security'], ['Setup', 'Setup']], st, 'log'), '0 1 150px'),
        field('Level', select([['all', 'All levels'], ['critical', 'Critical'], ['error', 'Error'], ['warning', 'Warning'], ['information', 'Information']], st, 'level'), '0 1 150px'),
        field('Last (hours)', select([['1', '1 hour'], ['6', '6 hours'], ['24', '24 hours'], ['72', '3 days'], ['168', '7 days'], ['720', '30 days']], st, 'hours'), '0 1 130px'),
        field('Text in message', input('text', st, 'text', 'optional'), '1 1 180px'),
        field('Event ID', input('number', st, 'eventId', 'optional'), '0 1 100px'),
        field('Max rows', select([['50', '50'], ['100', '100'], ['250', '250'], ['500', '500']], st, 'max'), '0 1 100px'),
        btn(st.busy ? 'Reading…' : 'Read log', load, { primary: true, disabled: st.busy })),
      h('span', { class: 'muted', style: { fontSize: '12px' } }, 'Read live from the computer each time; big logs or a long period can take up to a minute. The Security log needs the agent to run as SYSTEM (the default).'),
      st.err ? h('div', { class: 'banner', role: 'alert' }, st.err) : null,
      st.busy ? h('span', { class: 'muted' }, 'Asking the agent…') : null,
      d && !st.busy ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, ['Time', 'Level', 'Event', 'Source', 'Message'].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, d.entries.map((e, i) => h('tr', { style: { cursor: 'pointer' }, onclick: () => { st.open = st.open === i ? null : i; paint(); } },
          h('td', { style: { whiteSpace: 'nowrap' } }, when(e.time)),
          h('td', null, h('span', { class: `pill ${LEVEL[e.level] || 'neutral'}` }, e.level || '')),
          h('td', { class: 'mono' }, String(e.id)),
          h('td', null, e.provider || ''),
          h('td', { style: { whiteSpace: st.open === i ? 'pre-wrap' : 'nowrap', wordBreak: 'break-word', maxWidth: '560px', overflow: 'hidden', textOverflow: 'ellipsis' } }, e.message || '')))),
        d.entries.length ? null : h('caption', { class: 'empty' }, 'No events match.'))) : null,
      d?.truncated ? h('span', { class: 'muted' }, `Only the newest ${d.entries.length} events are shown. Narrow the period or the level to see others. Click a row to read the whole message.`) : d?.entries.length ? h('span', { class: 'muted' }, 'Click a row to read the whole message.') : null,
    ].filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint();
  return { el: root };
}
