import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead, searchBox, repaintKeepingSearch } from '../ui.js';
import { field, input, check, select, notice, btn } from '../forms.js';

const SHELLS = [['powershell', 'PowerShell'], ['cmd', 'Command Prompt'], ['python', 'Python'], ['shell', 'Shell'], ['nushell', 'Nushell'], ['deno', 'Deno']];
const PLATFORMS = [['windows', 'Windows'], ['linux', 'Linux'], ['darwin', 'macOS']];
const blank = () => ({ name: '', description: '', shell: 'powershell', script_body: '', default_timeout: '90', run_as_user: false, args: '', windows: true, linux: false, darwin: false });

export function scriptsView(root) {
  const st = { rows: null, q: '', notice: null, busy: false, adding: false, f: blank() };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('scripts', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading scripts…');

  async function run(fn, okMsg, after) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('scripts'); after?.(); st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const payload = () => {
    const f = st.f;
    return { name: f.name.trim(), description: f.description.trim() || null, shell: f.shell, script_body: f.script_body, default_timeout: Number(f.default_timeout) || 90,
      run_as_user: f.run_as_user, args: f.args.split('\n').map((x) => x.trim()).filter(Boolean), supported_platforms: PLATFORMS.map(([k]) => k).filter((k) => f[k]) };
  };
  const create = () => {
    const p = payload();
    if (!p.name) { st.notice = { ok: false, msg: 'Name is required.' }; return paint(); }
    if (!p.script_body.trim()) { st.notice = { ok: false, msg: 'Script body is required.' }; return paint(); }
    run(() => api('scripts', { method: 'POST', body: p }), 'Script added.', () => { st.f = blank(); st.adding = false; });
  };

  function form() {
    const f = st.f;
    const ta = h('textarea', { class: 'search mono', rows: 12, spellcheck: 'false', style: { maxWidth: 'none', flex: 'none', width: '100%', resize: 'vertical' }, placeholder: 'Script body' });
    ta.value = f.script_body; ta.addEventListener('input', () => { f.script_body = ta.value; });
    const args = h('textarea', { class: 'search mono', rows: 2, spellcheck: 'false', style: { maxWidth: 'none', flex: 'none', width: '100%' }, placeholder: 'One default argument per line (optional)' });
    args.value = f.args; args.addEventListener('input', () => { f.args = args.value; });
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, 'New script'),
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } }, field('Name', input('text', f, 'name', 'e.g. Clear temp files')), field('Shell', select(SHELLS, f, 'shell'), '1 1 160px'),
        field('Default timeout (seconds)', input('number', f, 'default_timeout', '90', { min: 1, max: 86400 }), '1 1 160px')),
      field('Description', input('text', f, 'description', 'What it does (optional)')),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, PLATFORMS.map(([k, l]) => check(l, f, k)), check('Run as logged-in user', f, 'run_as_user')),
      field('Script body', ta), field('Default arguments', args),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn('Add script', create, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.adding = false; st.notice = null; paint(); })));
  }

  function paint(err) {
    if (!st.rows) return;
    const q = st.q.trim().toLowerCase();
    const rows = st.rows.filter((s) => !q || `${s.name} ${s.description || ''}`.toLowerCase().includes(q));
    body.replaceChildren(...[
      pageHead('Scripts', `${st.rows.length} scripts`, btn('New script', () => { st.adding = true; st.notice = null; paint(); }, { primary: true, disabled: st.adding })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.adding ? form() : null,
      h('section', { class: 'card' }, searchBox(st.q, (v) => { st.q = v; repaintKeepingSearch(paint); }, 'Search scripts'),
        gridTable('cols-script', ['Name', 'Shell', 'Timeout', 'Platforms', ''], rows.map((s) => h('div', { class: 'tr cols-script' },
          h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '600' } }, s.name), s.description ? h('span', { class: 'muted' }, s.description) : null),
          h('div', null, SHELLS.find(([k]) => k === s.shell)?.[1] || s.shell), h('div', { class: 'mono' }, `${s.default_timeout}s`),
          h('div', { class: 'muted' }, (s.supported_platforms || []).join(', ') || 'all'),
          h('div', { style: { display: 'flex', justifyContent: 'flex-end' } }, btn('Delete', () => { if (confirm(`Delete script "${s.name}"? Checks or tasks that use it will stop working.`)) run(() => api(`scripts/${s.id}`, { method: 'DELETE' }), 'Script deleted.'); }, { disabled: st.busy, danger: true })))),
          q ? 'No scripts match.' : 'No scripts yet.'))].filter(Boolean));
  }
  return ld.stop;
}
