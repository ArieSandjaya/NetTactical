import { h } from '../dom.js';
import { api } from '../api.js';
import { ago, fmtDate } from '../format.js';
import { notice, btn } from '../forms.js';

const SHOWN = 300;   // rows drawn at once; searching narrows the list

// Installed applications of one asset: the last list the agent sent, a search box, and a button that asks the agent for a fresh list.
// Created once per page and reused, so the search text survives a repaint.
export function softwarePanel(agentId) {
  const st = { data: null, err: null, q: '', busy: false, notice: null };
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });
  const base = `agents/${encodeURIComponent(agentId)}/software`;

  async function load() { try { st.data = await api(base); st.err = null; } catch (e) { st.err = e.message; } paint(); }
  async function refresh() {
    st.busy = true; st.notice = null; paint();
    try { st.data = await api(`${base}/refresh`, { method: 'POST' }); st.notice = { ok: true, msg: `List updated: ${st.data.items.length} applications.` }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  const matches = (i) => !st.q.trim() || [i.name, i.publisher, i.version].some((v) => (v || '').toLowerCase().includes(st.q.trim().toLowerCase()));

  function paint() {
    if (st.err) { root.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    if (!st.data) { root.replaceChildren(h('div', { class: 'muted' }, 'Loading applications…')); return; }
    const items = st.data.items, found = items.filter(matches), shown = found.slice(0, SHOWN);
    const search = h('input', { type: 'search', class: 'search', placeholder: 'Search name, publisher or version', 'aria-label': 'Search applications', value: st.q, style: { maxWidth: 'none', flex: 'none', width: '100%' } });
    search.addEventListener('input', () => {
      st.q = search.value; paint();
      const box = root.querySelector('input[type=search]');   // paint() made a new box: put the caret back in it
      if (box) { box.focus(); box.setSelectionRange(st.q.length, st.q.length); }
    });
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap', justifyContent: 'space-between' } },
        h('span', { class: 'muted' }, st.data.updated_at ? `${items.length} applications · list received ${ago(st.data.updated_at)} (${fmtDate(st.data.updated_at)})` : 'No list received from this asset yet.'),
        btn(st.busy ? 'Asking the agent…' : 'Refresh from agent', refresh, { disabled: st.busy })),
      notice(st.notice),
      items.length ? search : null,
      items.length ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, ['Name', 'Version', 'Publisher', 'Installed'].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, shown.map((i) => h('tr', null, h('td', null, i.name), h('td', null, i.version || ''), h('td', null, i.publisher || ''), h('td', null, i.install_date || '')))),
        shown.length ? null : h('caption', { class: 'empty' }, 'No application matches.'))) : null,
      found.length > SHOWN ? h('span', { class: 'muted' }, `Showing the first ${SHOWN} of ${found.length}. Search to narrow the list.`) : null].filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint(); load();
  return { el: root };
}
