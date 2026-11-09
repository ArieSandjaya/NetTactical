const dt = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' });
const day = new Intl.DateTimeFormat('en-US', { day: 'numeric', month: 'short' });

export const fmtDate = (iso) => (iso ? dt.format(new Date(iso)) : '—');
export const fmtDay = (iso) => day.format(new Date(iso));

export function ago(iso, now = Date.now()) {
  if (!iso) return '—';
  const sec = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (sec < 60) return 'just now';
  const min = Math.round(sec / 60); if (min < 60) return `${min} min ago`;
  const hr = Math.round(min / 60); if (hr < 48) return `${hr} hr ago`;
  return `${Math.round(hr / 24)} days ago`;
}

// Score level from the API -> color class and text.
export const levelClass = (l) => ({ healthy: 'good', attention: 'warn', at_risk: 'crit' }[l] || 'warn');
export const levelWord = (l) => ({ healthy: 'Healthy', attention: 'Needs attention', at_risk: 'At risk' }[l] || '—');
// For per-client averages (the API only returns a number): default thresholds match HealthWeights.
export const scoreClass = (n) => (n >= 80 ? 'good' : n >= 60 ? 'warn' : 'crit');

export const sevClass = (s) => ({ error: 'crit', warning: 'warn', info: 'info' }[s] || 'info');
export const sevWord = (s) => ({ error: 'Critical', warning: 'Warning', info: 'Info' }[s] || s);

export const statusClass = (s) => ({ passing: 'good', failing: 'crit', pending: 'neutral' }[s] || 'neutral');
export const statusWord = (s) => ({ passing: 'Passing', failing: 'Failing', pending: 'Pending' }[s] || s);
export const syncWord = (s) => ({ synced: 'Synced', notsynced: 'Not synced', pendingdeletion: 'Pending deletion', initial: 'New' }[s] || s);
export const patchWord = (s) => ({ failed: 'Install failed', pending: 'Pending', ignored: 'Ignored' }[s] || s);
export const patchClass = (s) => ({ failed: 'crit', pending: 'warn', ignored: 'neutral' }[s] || 'neutral');

export const CHECK_TYPES = { cpuload: 'CPU', memory: 'Memory', diskspace: 'Disk', ping: 'Ping', winsvc: 'Windows service', script: 'Script', eventlog: 'Event log' };
export const checkType = (t) => CHECK_TYPES[t] || t;
export const KIND_LABELS = { check: 'Check', task: 'Scheduled task', availability: 'Availability', alert: 'Alert', patch: 'Windows patch', agent: 'Agent' };
export const HINTS = {
  check: 'Review the output above, fix the cause, then re-run the check.',
  task: 'Check stderr and the exit code. The task runs again on its schedule, or run it manually.',
  availability: 'Check the device\'s power and network, then the "Tactical RMM Agent" service and the local firewall.',
  patch: 'Schedule a reboot outside working hours, then reinstall the failed updates.',
  agent: 'Update the agent to the latest version.',
  alert: 'Review the alert message and follow your procedure.',
};
export const METRIC_NAMES = { cpu: 'CPU', memory: 'Memory', disk: 'Disk' };

export const patchSevClass = (s) => ({ critical: 'crit', important: 'warn', moderate: 'info' }[String(s || '').toLowerCase()] || 'neutral');
