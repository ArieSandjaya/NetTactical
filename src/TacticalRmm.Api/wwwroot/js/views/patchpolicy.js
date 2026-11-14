import { h } from '../dom.js';
import { api } from '../api.js';
import { field, check, select, notice, btn } from '../forms.js';

// Windows Update approval + schedule settings. Used for one agent and for an automation policy (same fields, same endpoint shape).
const APPROVE = [['inherit', 'Inherit'], ['approve', 'Approve'], ['ignore', 'Ignore']];
const FREQ = [['inherit', 'Inherit'], ['never', 'Never (manual only)'], ['daily', 'Daily'], ['weekly', 'Weekly'], ['monthly', 'Monthly']];
const REBOOT = [['inherit', 'Inherit'], ['never', 'Never'], ['required', 'Only if required'], ['always', 'Always']];
const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const word = (v) => ({ approve: 'approve', ignore: 'ignore', inherit: 'no rule', never: 'never', daily: 'daily', weekly: 'weekly', monthly: 'monthly', required: 'only if required', always: 'always' }[v] ?? v);

/** @param {{url: string, title: string, intro?: string, onSaved?: () => void}} o */
export function patchPolicyEditor(o) {
  const st = { data: null, form: null, notice: null, busy: false, err: null };
  const box = h('section', { class: 'card stack', style: { gap: '14px' } }, h('div', { class: 'muted' }, 'Loading settings…'));

  const toForm = (d) => ({ critical: d.critical, important: d.important, moderate: d.moderate, low: d.low, other: d.other, run_time_frequency: d.run_time_frequency,
    run_time_hour: String(d.run_time_hour), run_time_day: String(d.run_time_day), reboot_after_install: d.reboot_after_install, days: DAYS.map((_, i) => !!(d.run_time_weekdays & (1 << i))) });

  async function load() {
    try { st.data = await api(o.url); st.form = toForm(st.data.own); st.err = null; } catch (e) { st.err = e.message; }
    paint();
  }

  async function save() {
    const f = st.form;
    const run_time_weekdays = f.days.reduce((m, on, i) => (on ? m | (1 << i) : m), 0);
    st.busy = true; st.notice = null; paint();
    try {
      st.data = await api(o.url, { method: 'PUT', body: { critical: f.critical, important: f.important, moderate: f.moderate, low: f.low, other: f.other,
        run_time_frequency: f.run_time_frequency, run_time_hour: Number(f.run_time_hour), run_time_weekdays, run_time_day: Number(f.run_time_day), reboot_after_install: f.reboot_after_install } });
      st.form = toForm(st.data.own); st.notice = { ok: true, msg: 'Settings saved.' }; o.onSaved?.();
    } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  function effective() {
    const e = st.data.effective;
    const sched = e.frequency === 'never' ? 'no automatic install' : `${e.frequency} at ${String(e.hour).padStart(2, '0')}:00${e.frequency === 'weekly' ? ` (${DAYS.filter((_, i) => e.weekdays & (1 << i)).join(', ')})` : e.frequency === 'monthly' ? ` (day ${e.day})` : ''}`;
    return h('div', { class: 'banner', role: 'note', style: { background: 'var(--info-bg)', color: 'var(--info)' } },
      `In effect: critical ${word(e.critical)}, important ${word(e.important)}, moderate ${word(e.moderate)}, low ${word(e.low)}, other ${word(e.other)}. Install: ${sched}. Reboot: ${word(e.reboot)}.`);
  }

  function paint() {
    if (st.err) { box.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    if (!st.data) return;
    const f = st.form;
    const hours = Array.from({ length: 24 }, (_, i) => [i, `${String(i).padStart(2, '0')}:00`]);
    const sev = (label, key) => field(label, select(APPROVE, f, key), '1 1 130px');
    const freq = select(FREQ, f, 'run_time_frequency');
    freq.addEventListener('change', paint);
    box.replaceChildren(
      h('h2', { style: { fontSize: '16px' } }, o.title),
      h('p', { class: 'muted' }, o.intro ?? 'Approve updates automatically by severity and choose when approved updates are installed. "Inherit" takes the value from the automation policy; with nothing set, updates wait for a manual decision.'),
      effective(),
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } }, sev('Critical', 'critical'), sev('Important', 'important'), sev('Moderate', 'moderate'), sev('Low', 'low'), sev('Other / unrated', 'other')),
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
        field('Install schedule', freq, '1 1 200px'),
        f.run_time_frequency !== 'never' && f.run_time_frequency !== 'inherit' ? field('Time (agent\'s local time)', select(hours, f, 'run_time_hour'), '1 1 130px') : null,
        f.run_time_frequency === 'monthly' ? field('Day of month', (() => { const el = h('input', { type: 'number', min: '1', max: '31', class: 'search', style: { maxWidth: 'none', flex: 'none' }, value: f.run_time_day }); el.addEventListener('input', () => { f.run_time_day = el.value; }); return el; })(), '1 1 120px') : null,
        field('Reboot after install', select(REBOOT, f, 'reboot_after_install'), '1 1 180px')),
      f.run_time_frequency === 'weekly' ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '14px' } }, DAYS.map((d, i) => check(d, f.days, i))) : null,
      st.data.own.last_run_at ? h('span', { class: 'muted' }, `Last automatic install run: ${new Date(st.data.own.last_run_at).toLocaleString()}`) : null,
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } }, btn(st.busy ? 'Saving…' : 'Save settings', save, { primary: true, disabled: st.busy }), notice(st.notice)));
  }

  load();
  return box;
}
