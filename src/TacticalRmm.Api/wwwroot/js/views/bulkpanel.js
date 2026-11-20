import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, select, btn, notice } from '../forms.js';

const ACTIONS = [['command', 'Run command'], ['script', 'Run script'], ['reboot', 'Restart'], ['maintenance_on', 'Maintenance on'], ['maintenance_off', 'Maintenance off'], ['agent_update', 'Update agent'], ['install_msi', 'Install .msi']];
const STATUS = { queued: ['neutral', 'Waiting'], running: ['info', 'Running'], ok: ['good', 'Done'], failed: ['crit', 'Failed'], offline: ['warn', 'Offline'] };

// Actions on the assets ticked in the list: one form, one job, a live result per asset. Created once by the page and reused,
// so what is typed survives the list refreshing itself. setSelection() is called whenever a tick changes.
export function bulkPanel() {
  const st = { sel: [], open: false, action: 'command', cmd: '', shell: 'cmd', timeout: '30', scripts: null, scriptId: '', args: '', url: '', sha: '', msiArgs: '/qn /norestart', reinstall: false, msiTimeout: '900', busy: false, notice: null, job: null };
  const root = h('div', { class: 'card stack', style: { gap: '12px' } });
  let timer = null;

  const timeout = () => Math.max(1, Math.min(300, parseInt(st.timeout, 10) || 30));
  const stop = () => { if (timer) { clearTimeout(timer); timer = null; } };

  async function loadScripts() { try { st.scripts = await api('scripts'); } catch { st.scripts = []; } paint(); }

  async function poll() {
    stop();
    if (!st.job) return;
    try { st.job = await api(`bulk-actions/${st.job.id}`); } catch (e) { st.notice = { ok: false, msg: e.message }; st.busy = false; paint(); return; }
    if (st.job.state === 'running') timer = setTimeout(poll, 1500);
    else { st.busy = false; const n = st.job.targets.filter((t) => t.status === 'ok').length; st.notice = { ok: n === st.job.targets.length, msg: `Finished: ${n} of ${st.job.targets.length} succeeded.` }; }
    paint();
  }

  async function run() {
    const ids = st.sel.map((a) => a.agent_id);
    const body = { action: st.action, agent_ids: ids };
    if (st.action === 'command') { if (!st.cmd.trim()) { st.notice = { ok: false, msg: 'Enter a command.' }; paint(); return; } Object.assign(body, { cmd: st.cmd, shell: st.shell, timeout: timeout() }); }
    if (st.action === 'script') { if (!st.scriptId) { st.notice = { ok: false, msg: 'Choose a script.' }; paint(); return; } Object.assign(body, { script: +st.scriptId, args: st.args.split('\n').map((x) => x.trim()).filter(Boolean), timeout: timeout() }); }
    if (st.action === 'install_msi') {
      if (!st.url.trim()) { st.notice = { ok: false, msg: 'Enter the address of the .msi file.' }; paint(); return; }
      Object.assign(body, { url: st.url.trim(), sha256: st.sha.trim() || null, install_args: st.msiArgs.trim() || null, reinstall: st.reinstall, timeout: Math.max(60, Math.min(3600, parseInt(st.msiTimeout, 10) || 900)) });
    }
    const what = { install_msi: 'Install the .msi on', reboot: 'Restart', script: 'Run the script on', command: 'Run the command on', maintenance_on: 'Turn maintenance mode on for', maintenance_off: 'Turn maintenance mode off for', agent_update: 'Update the agent on' }[st.action];
    if (!confirm(`${what} ${ids.length} asset${ids.length === 1 ? '' : 's'}?${st.action === 'reboot' ? '\n\nUnsaved work on those computers will be lost.' : ''}`)) return;
    st.busy = true; st.notice = null; st.job = null; paint();
    try { st.job = await api('bulk-actions', { method: 'POST', body }); poll(); }
    catch (e) { st.busy = false; st.notice = { ok: false, msg: e.status === 403 ? 'You do not have permission for this action on every selected asset.' : e.message }; paint(); }
  }

  function paint() {
    if (!st.sel.length && !st.job) { root.style.display = 'none'; root.replaceChildren(); return; }
    root.style.display = '';
    const n = st.sel.length;
    const form = !st.open ? null : st.action === 'command'
      ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
        field('Command', input('text', st, 'cmd', 'ipconfig /all'), '3 1 320px'),
        field('Shell', select([['cmd', 'cmd'], ['powershell', 'PowerShell'], ['/bin/bash', 'bash (Linux)']], st, 'shell'), '0 1 150px'),
        field('Timeout (s)', input('number', st, 'timeout', '30'), '0 1 100px'))
      : st.action === 'script'
        ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
          field('Script', select([['', st.scripts ? '— choose a script —' : 'Loading…'], ...(st.scripts || []).map((x) => [x.id, `${x.name} (${x.shell})`])], st, 'scriptId'), '2 1 280px'),
          field('Arguments (one per line, optional)', input('text', st, 'args', ''), '2 1 240px'),
          field('Timeout (s)', input('number', st, 'timeout', '30'), '0 1 100px'))
        : st.action === 'reboot' ? h('span', { class: 'muted' }, 'Restarts every selected computer immediately. Unsaved work on them will be lost.')
          : st.action === 'install_msi' ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
            field('Address of the .msi (https:// or \\\\server\\share\\file.msi)', input('text', st, 'url', 'https://example.com/app.msi'), '3 1 340px'),
            field('SHA-256 (optional, recommended)', input('text', st, 'sha', ''), '2 1 260px'),
            field('Install options', input('text', st, 'msiArgs', '/qn /norestart'), '1 1 200px'),
            field('Time limit (s)', input('number', st, 'msiTimeout', '900'), '0 1 110px'),
            h('label', { style: { display: 'flex', gap: '6px', alignItems: 'center', fontSize: '13px' } },
              h('input', { type: 'checkbox', checked: st.reinstall, onchange: (e) => { st.reinstall = e.target.checked; } }), 'Reinstall even if already installed'),
            h('span', { class: 'muted', style: { flex: '1 1 100%', fontSize: '12px' } }, 'Each computer downloads the file itself (https) or copies it from the share, then runs msiexec silently as the agent account. The share must be readable by that account (computer account for SYSTEM).'))
          : st.action === 'agent_update' ? h('span', { class: 'muted' }, 'Windows agents that are online download the newest agent and restart themselves. Already up to date or offline assets are reported, not changed.')
          : h('span', { class: 'muted' }, st.action === 'maintenance_on' ? 'Selected assets stop opening new alerts and notifications. Alerts already open stay.' : 'Selected assets create alerts and notifications again.');
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '10px', alignItems: 'center', justifyContent: 'space-between' } },
        h('strong', null, n ? `${n} asset${n === 1 ? '' : 's'} selected` : 'Last bulk action'),
        h('div', { class: 'seg', role: 'group', 'aria-label': 'Bulk action' }, ACTIONS.map(([k, t]) => h('button', { 'aria-pressed': String(st.open && st.action === k), disabled: st.busy || !n, onclick: () => { st.action = k; st.open = true; st.notice = null; if (k === 'script' && !st.scripts) loadScripts(); paint(); } }, t)))),
      form,
      form ? h('div', null, btn(st.busy ? 'Running…' : 'Run on selected', run, { primary: true, disabled: st.busy || !n, danger: st.action === 'reboot' })) : null,
      notice(st.notice),
      st.job ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, ['Asset', 'Status', 'Result'].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, st.job.targets.map((t) => { const [cls, word] = STATUS[t.status] || ['neutral', t.status];
          return h('tr', null, h('td', null, t.hostname), h('td', null, h('span', { class: `pill ${cls}` }, word)),
            h('td', { style: { whiteSpace: 'pre-wrap', wordBreak: 'break-word', maxWidth: '520px' } }, t.message || '')); })))) : null,
    ].filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint();
  return {
    el: root,
    setSelection(list) { st.sel = list; if (!list.length && st.job?.state !== 'running') { st.open = false; } paint(); },
    stop,
  };
}
