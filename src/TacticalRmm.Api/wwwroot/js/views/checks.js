import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, check, select, notice, btn } from '../forms.js';
import { ago, statusClass, statusWord, sevClass, sevWord } from '../format.js';
import { scope, loadTargets, scopeSelect, ensureScope, listPath, ownerFields } from '../scope.js';

const TYPES = [['diskspace', 'Disk space'], ['cpuload', 'CPU load'], ['memory', 'Memory'], ['ping', 'Ping'], ['winsvc', 'Windows service'], ['script', 'Script'], ['eventlog', 'Event log']];
const SEV = [['warning', 'Warning'], ['error', 'Critical'], ['info', 'Info']];
const blank = () => ({ check_type: 'diskspace', alert_severity: 'warning', fails_b4_alert: '1', run_interval: '0', dashboard_alert: true, email_alert: false, text_alert: false,
  disk: 'C:', error_threshold: '10', warning_threshold: '20', ip: '', svc_name: '', svc_display_name: '', restart_if_stopped: false, pass_if_start_pending: false,
  script: '', timeout: '90', script_args: '', log_name: 'System', event_id: '0', event_type: 'ERROR', fail_when: 'contains', search_last_days: '1' });

const summary = (c) => ({ diskspace: `${c.disk} · alert below ${c.warning_threshold || '–'}% / ${c.error_threshold || '–'}% free`, cpuload: `warn ${c.warning_threshold || '–'}% · critical ${c.error_threshold || '–'}%`,
  memory: `warn ${c.warning_threshold || '–'}% · critical ${c.error_threshold || '–'}%`, ping: c.ip, winsvc: c.svc_name, script: `script #${c.script}`, eventlog: `${c.log_name} · ${c.event_type}` }[c.check_type] || '');

export function checksView(root) {
  const st = { targets: null, rows: null, notice: null, busy: false, adding: false, scripts: [], f: blank() };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, async (signal) => { const targets = await loadTargets(signal); const has = ensureScope(targets);
    return { targets, rows: has ? await api(listPath('checks'), { signal }) : [] }; }, (d, err) => { if (d) { st.targets = d.targets; st.rows = d.rows; } paint(err); }, 0, 'Loading checks…');

  const reload = async () => { st.rows = await api(listPath('checks')); };
  async function run(fn, okMsg, after) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); await reload(); after?.(); st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const changeScope = async () => { st.rows = null; st.adding = false; st.notice = null; paint(); try { await reload(); } catch (e) { st.rows = []; st.notice = { ok: false, msg: e.message }; } paint(); };
  const num = (v) => (v === '' || v == null ? null : Number(v));

  function payload() {
    const f = st.f, t = f.check_type;
    const p = { ...ownerFields(), check_type: t, alert_severity: f.alert_severity, fails_b4_alert: num(f.fails_b4_alert) ?? 1, run_interval: num(f.run_interval) ?? 0,
      dashboard_alert: f.dashboard_alert, email_alert: f.email_alert, text_alert: f.text_alert };
    if (t === 'diskspace') Object.assign(p, { disk: f.disk.trim(), error_threshold: num(f.error_threshold) ?? 0, warning_threshold: num(f.warning_threshold) ?? 0 });
    if (t === 'cpuload' || t === 'memory') Object.assign(p, { error_threshold: num(f.error_threshold) ?? 0, warning_threshold: num(f.warning_threshold) ?? 0 });
    if (t === 'ping') p.ip = f.ip.trim();
    if (t === 'winsvc') Object.assign(p, { svc_name: f.svc_name.trim(), svc_display_name: f.svc_display_name.trim() || f.svc_name.trim(), restart_if_stopped: f.restart_if_stopped, pass_if_start_pending: f.pass_if_start_pending });
    if (t === 'script') Object.assign(p, { script: num(f.script), timeout: num(f.timeout), script_args: f.script_args.split('\n').map((x) => x.trim()).filter(Boolean) });
    if (t === 'eventlog') Object.assign(p, { log_name: f.log_name.trim(), event_id: num(f.event_id) ?? 0, event_type: f.event_type, fail_when: f.fail_when, search_last_days: num(f.search_last_days) });
    return p;
  }
  async function startAdd() {
    st.adding = true; st.notice = null; st.f = blank(); paint();
    try { st.scripts = await api('scripts'); if (st.scripts.length) st.f.script = String(st.scripts[0].id); } catch { st.scripts = []; }
    paint();
  }

  function typeFields(f) {
    const t = f.check_type, thr = (a, b) => [field(a, input('number', f, 'warning_threshold', '', { min: 0, max: 99 }), '1 1 140px'), field(b, input('number', f, 'error_threshold', '', { min: 0, max: 99 }), '1 1 140px')];
    if (t === 'diskspace') return [field('Drive', input('text', f, 'disk', 'C:'), '1 1 100px'), ...thr('Warn when free space below (%)', 'Critical when free space below (%)')];
    if (t === 'cpuload' || t === 'memory') return thr('Warn above (%)', 'Critical above (%)');
    if (t === 'ping') return [field('Host or IP address', input('text', f, 'ip', 'e.g. 8.8.8.8 or fileserver'))];
    if (t === 'winsvc') return [field('Service name', input('text', f, 'svc_name', 'e.g. Spooler')), field('Display name (optional)', input('text', f, 'svc_display_name', '')), check('Restart if stopped', f, 'restart_if_stopped'), check('Pass if start pending', f, 'pass_if_start_pending')];
    if (t === 'script') {
      const args = h('textarea', { class: 'search mono', rows: 2, style: { maxWidth: 'none', flex: 'none', width: '100%' }, placeholder: 'One argument per line (optional)' });
      args.value = f.script_args; args.addEventListener('input', () => { f.script_args = args.value; });
      return [st.scripts.length ? field('Script', select(st.scripts.map((s) => [s.id, s.name]), f, 'script'), '2 1 240px') : h('div', { class: 'banner' }, 'There are no scripts yet. Add one on the Scripts page first.'),
        field('Timeout (seconds)', input('number', f, 'timeout', '90', { min: 1, max: 86400 }), '1 1 140px'), field('Arguments', args, '1 1 100%')];
    }
    return [field('Log', select([['Application', 'Application'], ['System', 'System'], ['Security', 'Security']], f, 'log_name'), '1 1 140px'), field('Event ID (0 = any)', input('number', f, 'event_id', '0', { min: 0 }), '1 1 140px'),
      field('Event type', select([['INFO', 'Information'], ['WARNING', 'Warning'], ['ERROR', 'Error'], ['AUDIT_SUCCESS', 'Audit success'], ['AUDIT_FAILURE', 'Audit failure']], f, 'event_type'), '1 1 160px'),
      field('Fail when', select([['contains', 'a matching event exists'], ['not_contains', 'no matching event exists']], f, 'fail_when'), '1 1 200px'), field('Look back (days)', input('number', f, 'search_last_days', '1', { min: 1, max: 3650 }), '1 1 140px')];
  }

  function form() {
    const f = st.f;
    const typeSel = select(TYPES, f, 'check_type'); typeSel.addEventListener('change', () => paint());
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, 'New check'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Type', typeSel, '1 1 200px'), ...typeFields(f)),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Severity', select(SEV, f, 'alert_severity'), '1 1 140px'),
        field('Failures before alert', input('number', f, 'fails_b4_alert', '1', { min: 1, max: 255 }), '1 1 140px'), field('Run every (seconds, 0 = default)', input('number', f, 'run_interval', '0', { min: 0, max: 86400 }), '1 1 180px')),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, check('Dashboard alert', f, 'dashboard_alert'), check('Email alert', f, 'email_alert'), check('Text alert', f, 'text_alert')),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Add check', () => run(() => api('checks', { method: 'POST', body: payload() }), 'Check added.', () => { st.adding = false; }), { primary: true, disabled: st.busy }),
        btn('Cancel', () => { st.adding = false; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.targets) return;
    const none = !st.targets.agents.length && !st.targets.policies.length;
    body.replaceChildren(...[
      pageHead('Checks', 'Monitors that run on the agent and raise alerts', btn('New check', startAdd, { primary: true, disabled: st.adding || none })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      none ? h('div', { class: 'card empty' }, 'No agents or policies yet.') : h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } }, h('span', { class: 'eyebrow' }, 'Showing'), scopeSelect(st.targets, changeScope)),
      st.adding ? form() : null,
      none ? null : h('section', { class: 'card' }, st.rows == null ? h('div', { class: 'loading' }, 'Loading…') : gridTable('cols-chk', ['Check', 'Status', 'Severity', 'Last run', ''], st.rows.map((c) => h('div', { class: 'tr cols-chk' },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '600' } }, c.name || c.check_type), h('span', { class: 'muted' }, `${TYPES.find(([k]) => k === c.check_type)?.[1] || c.check_type} · ${summary(c)}`),
          c.managed_by_policy ? h('span', { class: 'pill info', style: { alignSelf: 'flex-start' } }, 'From policy') : null),
        h('div', null, h('span', { class: `pill ${statusClass(c.status)}` }, statusWord(c.status))), h('div', null, h('span', { class: `pill ${sevClass(c.alert_severity)}` }, sevWord(c.alert_severity))),
        h('div', { class: 'mono' }, ago(c.last_run)),
        h('div', { style: { display: 'flex', justifyContent: 'flex-end' } }, c.managed_by_policy ? null : btn('Delete', () => { if (confirm(`Delete check "${c.name || c.check_type}"?`)) run(() => api(`checks/${c.id}`, { method: 'DELETE' }), 'Check deleted.'); }, { disabled: st.busy, danger: true })))),
        scope.kind === 'agent' ? 'This agent has no checks.' : 'This policy has no checks.'))].filter(Boolean));
  }
  return ld.stop;
}
