import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, select, notice, btn } from '../forms.js';

// [action, label, what it removes, fewest days, default days]
const ACTIONS = [
  ['prune_audit', 'Audit log', 'Audit log entries older than this.', 30, 90],
  ['prune_alerts', 'Resolved alerts', 'Alerts that were resolved longer ago than this. Open alerts stay.', 7, 30],
  ['prune_history', 'Command and script history', 'Per-asset history of commands and scripts older than this.', 7, 30],
  ['prune_tokens', 'Expired sign-in tokens', 'Login and API tokens that expired longer ago than this.', 1, 1],
  ['stale_agents', 'Assets that stopped reporting', 'Assets not seen for this long are removed from the database (up to 200 per run). Nothing is uninstalled on the computer.', 30, 90],
];
const why = (e) => (e.status === 403 ? 'You do not have permission for server maintenance.' : e.message);

// Housekeeping: preview how many rows match, then delete them. Every run is written to the audit log.
export function maintenanceView(root) {
  const st = { form: Object.fromEntries(ACTIONS.map((a) => [a[0], String(a[4])])), preview: {}, notice: {}, busy: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let alive = true;

  async function look(a) {
    st.busy = a; st.notice[a] = null; paint();
    try { st.preview[a] = await api(`maintenance/preview?action=${a}&days=${encodeURIComponent(st.form[a])}`); }
    catch (e) { st.preview[a] = null; st.notice[a] = { ok: false, msg: why(e) }; }
    st.busy = null; if (alive) paint();
  }

  async function run(a, label) {
    const n = st.preview[a]?.count;
    if (!confirm(`Remove ${n} item(s): ${label}, older than ${st.form[a]} days?\n\nThis cannot be undone.`)) return;
    st.busy = a; st.notice[a] = null; paint();
    try { const r = await api('maintenance/run', { method: 'POST', body: { action: a, days: Number(st.form[a]) } }); st.notice[a] = { ok: true, msg: `${r.count} removed.` }; st.preview[a] = null; }
    catch (e) { st.notice[a] = { ok: false, msg: why(e) }; }
    st.busy = null; if (alive) paint();
  }

  function card([a, label, help, min]) {
    const f = st.form;
    const p = st.preview[a];
    const days = input('number', f, a, String(min));
    days.min = String(min);
    days.addEventListener('input', () => { st.preview[a] = null; });
    return h('section', { class: 'card stack', style: { gap: '10px' } },
      h('strong', null, label), h('div', { class: 'muted' }, help),
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'flex-end', flexWrap: 'wrap' } },
        field(`Older than (days, at least ${min})`, days, '0 1 200px'),
        btn(st.busy === a ? 'Counting…' : 'Count', () => look(a), { disabled: st.busy !== null }),
        p ? btn(`Remove ${p.count}`, () => run(a, label), { danger: true, disabled: st.busy !== null || p.count === 0 }) : null),
      p ? h('div', { class: 'muted' }, `${p.count} item(s) match.`) : null, notice(st.notice[a]));
  }

  const paint = () => body.replaceChildren(
    h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Server maintenance'),
      h('p', { class: 'muted' }, 'Clean up old records to keep the database small. Count first, then remove. Every run is written to the audit log.'))),
    ...ACTIONS.map(card));
  paint();
  return () => { alive = false; };
}
