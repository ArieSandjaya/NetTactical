import { h } from '../dom.js';
import { api } from '../api.js';
import { ago, levelClass, levelWord } from '../format.js';
import { chip, seg, searchBox, loader } from '../ui.js';

const LEVELS = [['all', 'All'], ['at_risk', 'At risk'], ['attention', 'Needs attention'], ['healthy', 'Healthy'], ['offline', 'Offline']];
const SORTS = { score: (a, b) => a.score - b.score || a.hostname.localeCompare(b.hostname),
  name: (a, b) => a.hostname.localeCompare(b.hostname),
  seen: (a, b) => (new Date(b.last_seen || 0) - new Date(a.last_seen || 0)) };

export function assetsView(root) {
  const st = { client: null, level: 'all', q: '', sort: 'score', data: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let listEl = null, countEl = null;

  const ld = loader(body, (signal) => api('fleet/assets', { signal }), (d, err) => {
    if (d) st.data = d;
    paint(err);
  }, 60000, 'Loading assets…');

  function visible() {
    const q = st.q.trim().toLowerCase();
    return st.data.assets.filter((a) =>
      (st.client === null || a.client === st.client) &&
      (st.level === 'all' || (st.level === 'offline' ? !a.online : a.level === st.level)) &&
      (!q || `${a.hostname} ${a.client} ${a.site} ${a.os}`.toLowerCase().includes(q))).sort(SORTS[st.sort]);
  }

  // The search box must keep focus while typing, so only the table is repainted for text input.
  function paintList() {
    const rows = visible();
    countEl.textContent = `${rows.length} of ${st.data.assets.length} assets`;
    listEl.replaceChildren(h('div', { class: 'grid-table' },
      h('div', { class: 'tr th cols-asset' }, h('div', null, 'Asset'), h('div', null, 'Score'), h('div', null, 'Status'), h('div', null, 'Main issue'),
        h('div', { style: { textAlign: 'right' } }, 'Updates'), h('div', { style: { textAlign: 'right' } }, 'Alerts'), h('div', { style: { textAlign: 'right' } }, 'Last seen')),
      rows.map((a) => h('div', { class: 'tr cols-asset' },
        h('div', { class: 'stack', style: { gap: '2px' } },
          h('a', { class: 'mono', href: `#/device/${encodeURIComponent(a.agent_id)}`, style: { fontWeight: '500' } }, a.hostname),
          h('span', { class: 'muted', style: { fontSize: '12px' } }, `${a.client} · ${a.site} · ${a.os || 'Unknown OS'}`)),
        h('div', null, h('span', { class: `score ${levelClass(a.level)}`, title: levelWord(a.level) }, String(a.score))),
        h('div', null, h('span', { class: `pill ${a.online ? 'good' : 'crit'}` }, a.online ? 'Online' : 'Offline')),
        h('div', null, a.top_reason ? h('span', { class: 'chipx' }, a.top_reason) : h('span', { class: 'muted' }, '—')),
        h('div', { class: 'mono', style: { textAlign: 'right' } }, String(a.pending_updates)),
        h('div', { class: 'mono', style: { textAlign: 'right', color: a.open_alerts ? 'var(--crit)' : '' } }, String(a.open_alerts)),
        h('div', { class: 'muted', style: { textAlign: 'right', fontSize: '12px' } }, ago(a.last_seen)))),
      rows.length === 0 ? h('div', { class: 'empty' }, 'No assets match these filters.') : null));
  }

  function paint(err) {
    if (!st.data) return;
    const counts = {}; for (const a of st.data.assets) counts[a.client] = (counts[a.client] || 0) + 1;
    const clients = Object.keys(counts).sort();
    listEl = h('div', { class: 'scroll-x' }); countEl = h('div', { class: 'muted' });
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      h('div', { class: 'page-head' },
        h('div', { class: 'stack' }, h('h1', null, 'Assets'), countEl),
        h('div', { class: 'chips' },
          chip('All', st.data.assets.length, st.client === null, () => { st.client = null; paint(); }),
          clients.map((c) => chip(c, counts[c], st.client === c, () => { st.client = st.client === c ? null : c; paint(); })))),
      h('div', { class: 'card' },
        h('div', { class: 'page-head', style: { alignItems: 'center' } },
          searchBox(st.q, (v) => { st.q = v; paintList(); }, 'Search assets'),
          h('div', { class: 'tabs-inline' },
            seg(LEVELS, st.level, (k) => { st.level = k; paint(); }, 'Health'),
            seg([['score', 'Lowest score'], ['name', 'Name'], ['seen', 'Last seen']], st.sort, (k) => { st.sort = k; paint(); }, 'Sort'))),
        listEl)].filter(Boolean));
    paintList();
  }
  return ld.stop;
}
