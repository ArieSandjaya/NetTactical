import { h } from '../dom.js';
import { api } from '../api.js';
import { notice, btn } from '../forms.js';
import { patchPolicyEditor } from './patchpolicy.js';

// Windows Update management for one agent: decide per update, scan, install approved now, and edit the agent's patch settings.
const DECISION = { approve: ['good', 'Approved'], ignore: ['neutral', 'Ignored'], nothing: ['warn', 'Not decided'] };

export function patchPanel(agentId) {
  const st = { rows: null, sel: new Set(), notice: null, busy: false, err: null, settings: false, editor: null };
  const root = h('div', { class: 'pad stack', style: { gap: '16px' } });
  const base = `agents/${agentId}/winupdates`;

  async function load() { try { st.rows = await api(base); st.err = null; } catch (e) { st.err = e.message; } paint(); }

  async function run(fn) {
    st.busy = true; st.notice = null; paint();
    try { st.notice = { ok: true, msg: await fn() }; await load(); } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const decide = (action) => run(async () => { const r = await api(`${base}/action`, { method: 'PUT', body: { guids: [...st.sel], action } }); st.sel.clear(); return `${r} update(s) set to ${action}.`; });
  const scan = () => run(async () => { await api(`${base}/scan`, { method: 'POST' }); return 'Scan requested. The list refreshes when the agent reports back (reload in a minute).'; });
  const install = () => run(async () => {
    const r = await api(`${base}/install`, { method: 'POST' });
    return r.requested === 0 ? 'No approved updates are waiting.' : r.agent_online ? `Installing ${r.requested} approved update(s) now.` : `The agent is offline; ${r.requested} approved update(s) will install at the next schedule.`;
  });

  function statusPill(u) {
    if (u.installed) return h('span', { class: 'pill good' }, 'Installed');
    if (u.result === 'failed') return h('span', { class: 'pill crit' }, 'Failed');
    return h('span', { class: 'pill info' }, 'Pending');
  }

  function paint() {
    if (st.err) { root.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    if (!st.rows) { root.replaceChildren(h('div', { class: 'muted' }, 'Loading updates…')); return; }
    const open = st.rows.filter((u) => !u.installed);
    const waiting = st.rows.filter((u) => u.action === 'approve' && !u.installed).length;
    const allBox = h('input', { type: 'checkbox', 'aria-label': 'Select all pending updates' });
    allBox.checked = open.length > 0 && open.every((u) => st.sel.has(u.guid));
    allBox.addEventListener('change', () => { if (allBox.checked) open.forEach((u) => st.sel.add(u.guid)); else st.sel.clear(); paint(); });
    const none = st.sel.size === 0;
    if (!st.editor) st.editor = patchPolicyEditor({ url: `${base}/policy`, title: 'Update settings for this computer' });
    root.replaceChildren(
      h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' } },
        btn('Scan now', scan, { disabled: st.busy }), btn(`Install approved now (${waiting})`, install, { primary: true, disabled: st.busy || waiting === 0 }),
        h('span', { class: 'muted' }, '|'), btn('Approve', () => decide('approve'), { disabled: st.busy || none }), btn('Ignore', () => decide('ignore'), { disabled: st.busy || none }),
        btn('Reset', () => decide('nothing'), { disabled: st.busy || none }), h('span', { class: 'muted' }, none ? 'Select updates to decide.' : `${st.sel.size} selected`),
        h('span', { style: { flex: '1' } }), btn(st.settings ? 'Hide settings' : 'Settings', () => { st.settings = !st.settings; paint(); })),
      notice(st.notice),
      st.settings ? st.editor : null,
      st.rows.length === 0 ? h('div', { class: 'empty' }, 'No updates reported yet. Use "Scan now".') :
        h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table', role: 'table' },
          h('div', { class: 'tr th cols-patchm', role: 'row' }, h('div', null, allBox), h('div', null, 'KB'), h('div', null, 'Update'), h('div', null, 'Severity'), h('div', null, 'Decision'), h('div', null, 'Status')),
          st.rows.map((u) => {
            const cb = h('input', { type: 'checkbox', disabled: u.installed, 'aria-label': `Select ${u.kb || u.title}` });
            cb.checked = st.sel.has(u.guid);
            cb.addEventListener('change', () => { if (cb.checked) st.sel.add(u.guid); else st.sel.delete(u.guid); paint(); });
            const [tone, text] = u.installed ? ['neutral', '—'] : DECISION[u.action] ?? ['neutral', u.action];
            return h('div', { class: 'tr cols-patchm', role: 'row' }, h('div', null, cb), h('div', { class: 'mono' }, u.kb || '—'), h('div', { style: { overflowWrap: 'anywhere' } }, u.title || '—'),
              h('div', null, u.severity || '—'), h('div', null, h('span', { class: `pill ${tone}` }, text)), h('div', null, statusPill(u)));
          }))));
  }

  load();
  return root;
}
