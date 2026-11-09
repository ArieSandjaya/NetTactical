// Actions on one asset: run a command, run a script, reboot. State lives in the device view so repaints keep it.
import { h } from './dom.js';
import { api } from './api.js';
import { field, input, select, btn } from './forms.js';

export const newActionsState = () => ({ open: false, tab: 'cmd', cmd: '', shell: 'cmd', timeout: '30', scripts: null, scriptId: '', args: '', busy: false, out: null, ok: null });

const show = (r) => (typeof r === 'string' ? r : JSON.stringify(r, null, 2));

export function actionsCard(s, id, plat, repaint) {
  const toggle = btn(s.open ? 'Hide actions' : 'Actions…', () => { s.open = !s.open; if (s.open && !s.scripts) loadScripts(); repaint(); });
  if (!s.open) return toggle;

  async function loadScripts() {
    try { s.scripts = await api('scripts'); } catch { s.scripts = []; }
    repaint();
  }
  async function exec(fn) {
    s.busy = true; s.out = null; s.ok = null; repaint();
    try { const r = await fn(); s.out = show(r ?? 'ok'); s.ok = !(typeof r === 'string' && ['timeout', 'natsdown', 'malformed'].includes(r)); }
    catch (e) { s.out = e.message; s.ok = false; }
    s.busy = false; repaint();
  }
  const timeout = () => Math.max(1, Math.min(600, parseInt(s.timeout, 10) || 30));
  const tabs = h('div', { class: 'seg', role: 'group', 'aria-label': 'Action' }, [['cmd', 'Run command'], ['script', 'Run script'], ['reboot', 'Reboot']]
    .map(([k, t]) => h('button', { 'aria-pressed': String(k === s.tab), onclick: () => { s.tab = k; s.out = null; repaint(); } }, t)));

  let panel;
  if (s.tab === 'cmd') {
    panel = h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
      field('Command', input('text', s, 'cmd', plat === 'linux' ? 'uname -a' : 'ipconfig /all'), '3 1 320px'),
      field('Shell', select(plat === 'linux' ? [['/bin/bash', 'bash']] : [['cmd', 'cmd'], ['powershell', 'PowerShell']], s, 'shell'), '0 1 140px'),
      field('Timeout (s)', input('number', s, 'timeout', '30'), '0 1 100px'),
      btn(s.busy ? 'Running…' : 'Run', () => { if (!s.cmd.trim()) return; exec(() => api(`agents/${id}/cmd`, { method: 'POST', body: { cmd: s.cmd, shell: plat === 'linux' ? '/bin/bash' : s.shell, timeout: timeout() } })); }, { primary: true, disabled: s.busy }));
  } else if (s.tab === 'script') {
    const opts = [['', s.scripts ? '— choose a script —' : 'Loading…'], ...(s.scripts || []).map((x) => [x.id, `${x.name} (${x.shell})`])];
    panel = h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
      field('Script', select(opts, s, 'scriptId'), '2 1 280px'), field('Arguments (one per line, optional)', input('text', s, 'args', ''), '2 1 240px'),
      field('Timeout (s)', input('number', s, 'timeout', '30'), '0 1 100px'),
      btn(s.busy ? 'Running…' : 'Run', () => { if (!s.scriptId) return;
        exec(() => api(`agents/${id}/runscript`, { method: 'POST', body: { script: +s.scriptId, output: 'wait', args: s.args.split('\n').map((x) => x.trim()).filter(Boolean), run_as_user: false, env_vars: [], timeout: timeout() } })); }, { primary: true, disabled: s.busy }));
  } else {
    panel = h('div', { style: { display: 'flex', gap: '12px', alignItems: 'center', flexWrap: 'wrap' } },
      h('span', { class: 'muted' }, 'Restarts the machine immediately. Unsaved work on it will be lost.'),
      btn(s.busy ? 'Rebooting…' : 'Reboot now', () => { if (confirm('Reboot this asset now?')) exec(() => api(`agents/${id}/reboot`, { method: 'POST' })); }, { danger: true, disabled: s.busy }));
  }
  return h('div', { class: 'stack', style: { gap: '12px', flexBasis: '100%' } }, h('div', null, toggle), tabs, panel,
    s.out == null ? null : h('pre', { class: `act-out ${s.ok ? '' : 'bad'}`, role: 'status' }, s.out));
}
