import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, check, select, notice, btn } from '../forms.js';
import { ago, fmtDate, statusClass, statusWord, syncWord, sevClass, sevWord } from '../format.js';
import { scope, loadTargets, scopeSelect, ensureScope, listPath, ownerFields } from '../scope.js';

const TYPES = [['manual', 'Run manually'], ['daily', 'Daily'], ['weekly', 'Weekly'], ['runonce', 'Run once']];
const SEV = [['info', 'Info'], ['warning', 'Warning'], ['error', 'Critical']];
const SHELLS = [['powershell', 'PowerShell'], ['cmd', 'Command Prompt'], ['python', 'Python']];
const DAYS = [['Sun', 1], ['Mon', 2], ['Tue', 4], ['Wed', 8], ['Thu', 16], ['Fri', 32], ['Sat', 64]];   // Windows Task Scheduler bit mask
const blank = () => ({ name: '', task_type: 'daily', when: '', daily_interval: '1', weekly_interval: '1', days: { Mon: true }, action: 'cmd', shell: 'powershell', command: '', script: '', script_args: '', timeout: '120',
  alert_severity: 'info', dashboard_alert: false, email_alert: false, text_alert: false, enabled: true, continue_on_error: true });

const when = (t) => ({ manual: 'Manual', daily: `Daily${t.daily_interval > 1 ? ` (every ${t.daily_interval} days)` : ''}`, weekly: `Weekly${t.weekly_interval > 1 ? ` (every ${t.weekly_interval} weeks)` : ''}`,
  runonce: 'Once', checkfailure: 'On check failure', monthly: 'Monthly', monthlydow: 'Monthly (weekday)' }[t.task_type] || t.task_type) + (t.run_time_date && t.task_type !== 'manual' ? ` · ${fmtDate(t.run_time_date)}` : '');

export function tasksView(root) {
  const st = { targets: null, rows: null, notice: null, busy: false, adding: false, scripts: [], f: blank() };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, async (signal) => { const targets = await loadTargets(signal); const has = ensureScope(targets);
    return { targets, rows: has ? await api(listPath('tasks'), { signal }) : [] }; }, (d, err) => { if (d) { st.targets = d.targets; st.rows = d.rows; } paint(err); }, 0, 'Loading tasks…');

  const reload = async () => { st.rows = await api(listPath('tasks')); };
  async function run(fn, okMsg, after) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); await reload(); after?.(); st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const changeScope = async () => { st.rows = null; st.adding = false; st.notice = null; paint(); try { await reload(); } catch (e) { st.rows = []; st.notice = { ok: false, msg: e.message }; } paint(); };

  function payload() {
    const f = st.f, p = { ...ownerFields(), name: f.name.trim(), task_type: f.task_type, enabled: f.enabled, continue_on_error: f.continue_on_error,
      alert_severity: f.alert_severity, dashboard_alert: f.dashboard_alert, email_alert: f.email_alert, text_alert: f.text_alert };
    if (f.task_type !== 'manual') { if (!f.when) throw new Error('Choose when the task should run.'); p.run_time_date = new Date(f.when).toISOString(); }
    if (f.task_type === 'daily') p.daily_interval = Number(f.daily_interval) || 1;
    if (f.task_type === 'weekly') {
      p.weekly_interval = Number(f.weekly_interval) || 1;
      p.run_time_bit_weekdays = DAYS.reduce((m, [d, bit]) => m + (f.days[d] ? bit : 0), 0);
      if (!p.run_time_bit_weekdays) throw new Error('Choose at least one weekday.');
    }
    const timeout = Number(f.timeout) || 120;
    p.actions = [f.action === 'cmd' ? { type: 'cmd', shell: f.shell, command: f.command.trim(), timeout }
      : { type: 'script', script: Number(f.script), script_args: f.script_args.split('\n').map((x) => x.trim()).filter(Boolean), timeout }];
    if (f.action === 'script' && !f.script) throw new Error('Choose a script.');
    return p;
  }
  const create = () => { let p; try { p = payload(); } catch (e) { st.notice = { ok: false, msg: e.message }; return paint(); }
    run(() => api('tasks', { method: 'POST', body: p }), 'Task added.', () => { st.adding = false; }); };
  async function startAdd() {
    st.adding = true; st.notice = null; st.f = blank(); paint();
    try { st.scripts = await api('scripts'); if (st.scripts.length) st.f.script = String(st.scripts[0].id); } catch { st.scripts = []; }
    paint();
  }

  function form() {
    const f = st.f;
    const typeSel = select(TYPES, f, 'task_type'); typeSel.addEventListener('change', () => paint());
    const actSel = select([['cmd', 'Run a command'], ['script', 'Run a script']], f, 'action'); actSel.addEventListener('change', () => paint());
    const cmd = h('textarea', { class: 'search mono', rows: 3, spellcheck: 'false', style: { maxWidth: 'none', flex: 'none', width: '100%' }, placeholder: 'Command to run' });
    cmd.value = f.command; cmd.addEventListener('input', () => { f.command = cmd.value; });
    const args = h('textarea', { class: 'search mono', rows: 2, style: { maxWidth: 'none', flex: 'none', width: '100%' }, placeholder: 'One argument per line (optional)' });
    args.value = f.script_args; args.addEventListener('input', () => { f.script_args = args.value; });
    const dt = h('input', { type: 'datetime-local', class: 'search', style: { maxWidth: 'none', flex: 'none' }, value: f.when }); dt.addEventListener('input', () => { f.when = dt.value; });
    const days = h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, DAYS.map(([d]) => check(d, f.days, d)));
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, 'New task'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Name', input('text', f, 'name', 'e.g. Weekly cleanup'), '2 1 240px'), field('Schedule', typeSel, '1 1 160px'),
        f.task_type !== 'manual' ? field('Start (your local time)', dt, '1 1 220px') : null,
        f.task_type === 'daily' ? field('Every (days)', input('number', f, 'daily_interval', '1', { min: 1, max: 365 }), '1 1 120px') : null,
        f.task_type === 'weekly' ? field('Every (weeks)', input('number', f, 'weekly_interval', '1', { min: 1, max: 52 }), '1 1 120px') : null),
      f.task_type === 'weekly' ? field('On', days) : null,
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Action', actSel, '1 1 180px'),
        f.action === 'cmd' ? field('Shell', select(SHELLS, f, 'shell'), '1 1 160px') : (st.scripts.length ? field('Script', select(st.scripts.map((s) => [s.id, s.name]), f, 'script'), '1 1 240px') : h('div', { class: 'banner' }, 'There are no scripts yet. Add one on the Scripts page first.')),
        field('Timeout (seconds)', input('number', f, 'timeout', '120', { min: 1, max: 86400 }), '1 1 140px')),
      f.action === 'cmd' ? field('Command', cmd) : field('Arguments', args),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Alert severity', select(SEV, f, 'alert_severity'), '1 1 160px')),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, check('Enabled', f, 'enabled'), check('Continue if an action fails', f, 'continue_on_error'), check('Dashboard alert', f, 'dashboard_alert'), check('Email alert', f, 'email_alert'), check('Text alert', f, 'text_alert')),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Add task', create, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.adding = false; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.targets) return;
    const none = !st.targets.agents.length && !st.targets.policies.length, onAgent = scope.kind === 'agent';
    body.replaceChildren(...[
      pageHead('Tasks', 'Scheduled commands and scripts that run on the agent', btn('New task', startAdd, { primary: true, disabled: st.adding || none })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      none ? h('div', { class: 'card empty' }, 'No agents or policies yet.') : h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } }, h('span', { class: 'eyebrow' }, 'Showing'), scopeSelect(st.targets, changeScope)),
      st.adding ? form() : null,
      none ? null : h('section', { class: 'card' }, st.rows == null ? h('div', { class: 'loading' }, 'Loading…') : gridTable('cols-tsk', ['Task', 'Status', 'Last run', 'Enabled', ''], st.rows.map((t) => h('div', { class: 'tr cols-tsk' },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '600' } }, t.name), h('span', { class: 'muted' }, when(t)),
          h('span', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap' } }, h('span', { class: `pill ${sevClass(t.alert_severity)}` }, sevWord(t.alert_severity)), onAgent ? h('span', { class: 'pill neutral' }, syncWord(t.sync_status)) : null)),
        h('div', null, h('span', { class: `pill ${statusClass(t.status)}` }, statusWord(t.status))), h('div', { class: 'mono' }, ago(t.last_run)),
        h('div', null, check(t.enabled ? 'On' : 'Off', { v: t.enabled }, 'v', () => run(() => api(`tasks/${t.id}`, { method: 'PUT', body: { enabled: !t.enabled } }), t.enabled ? 'Task disabled.' : 'Task enabled.'), st.busy)),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          onAgent ? btn('Run now', () => run(() => api(`tasks/${t.id}/run`, { method: 'POST', body: { agent_id: scope.id } }), `"${t.name}" was sent to the agent.`), { disabled: st.busy }) : null,
          btn('Delete', () => { if (confirm(`Delete task "${t.name}"?`)) run(() => api(`tasks/${t.id}`, { method: 'DELETE' }), 'Task deleted.'); }, { disabled: st.busy, danger: true })))),
        onAgent ? 'This agent has no tasks.' : 'This policy has no tasks.'))].filter(Boolean));
  }
  return ld.stop;
}
