import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead, searchBox, chip, repaintKeepingSearch } from '../ui.js';
import { fmtDate } from '../format.js';
import { btn } from '../forms.js';

const SIZE = 50;
const ACTIONS = { login: 'Login', failed_login: 'Failed login', add: 'Add', modify: 'Modify', delete: 'Delete', agent_install: 'Agent install', execute_script: 'Run script', execute_command: 'Run command', bulk_action: 'Bulk action', view: 'View' };
const actionClass = (a) => (a === 'failed_login' || a === 'delete' ? 'crit' : a === 'execute_command' || a === 'execute_script' || a === 'bulk_action' ? 'warn' : a === 'login' ? 'good' : 'neutral');

export function auditView(root) {
  const st = { page: 1, data: null, q: '', action: 'all' };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api(`logs/audit?page=${st.page}&pageSize=${SIZE}`, { signal }), (d, err) => { if (d) st.data = d; paint(err); }, 0, 'Loading audit log…');
  const go = (p) => { st.page = p; ld.reload(); };

  function paint(err) {
    if (!st.data) return;
    const q = st.q.trim().toLowerCase();
    const rows = st.data.results.filter((r) => (st.action === 'all' || r.action === st.action)
      && (!q || `${r.username} ${r.agent || ''} ${r.message || ''} ${r.object_type}`.toLowerCase().includes(q)));
    const pages = Math.max(1, Math.ceil(st.data.total / SIZE));
    const seen = [...new Set(st.data.results.map((r) => r.action))];
    body.replaceChildren(...[
      pageHead('Audit log', `${st.data.total} entries · page ${st.page} of ${pages}`),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      h('section', { class: 'card' },
        h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'center' } }, searchBox(st.q, (v) => { st.q = v; repaintKeepingSearch(paint); }, 'Search this page'),
          h('div', { class: 'chips' }, chip('All', null, st.action === 'all', () => { st.action = 'all'; paint(); }),
            seen.map((a) => chip(ACTIONS[a] || a, null, st.action === a, () => { st.action = a; paint(); })))),
        gridTable('cols-audit', ['Time', 'User', 'Action', 'Details'], rows.map((r) => h('div', { class: 'tr cols-audit' },
          h('div', { class: 'mono' }, fmtDate(r.entry_time)), h('div', null, r.username),
          h('div', null, h('span', { class: `pill ${actionClass(r.action)}` }, ACTIONS[r.action] || r.action)),
          h('div', { style: { overflowWrap: 'anywhere' } }, r.message || '—', r.agent ? h('span', { class: 'muted' }, ` · ${r.agent}`) : null, r.debug_info ? h('span', { class: 'muted mono' }, ` · ${r.debug_info}`) : null))), 'No entries.'),
        h('div', { style: { display: 'flex', gap: '8px', justifyContent: 'flex-end' } },
          btn('Newer', () => go(st.page - 1), { disabled: st.page <= 1 }), btn('Older', () => go(st.page + 1), { disabled: st.page >= pages })))].filter(Boolean));
  }
  return ld.stop;
}
