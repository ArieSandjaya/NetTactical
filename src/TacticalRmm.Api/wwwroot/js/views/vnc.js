import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';

// VNC passwords: the lowest priority number is the default, the others are fallbacks tried in order only if the default is rejected.
export function vncView(root) {
  const st = { rows: null, notice: null, busy: false, editing: null, form: { label: '', password: '', priority: '' } };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  const ld = loader(body, (signal) => api('vnc/credentials', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading VNC passwords…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('vnc/credentials'); st.editing = null; st.form = { label: '', password: '', priority: '' }; st.notice = { ok: true, msg: okMsg }; }
    catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  const num = (v) => (String(v).trim() === '' || Number.isNaN(Number(v)) ? undefined : Math.trunc(Number(v)));
  const field = (label, input) => h('label', { class: 'stack', style: { gap: '4px', flex: '1 1 160px' } }, h('span', { class: 'eyebrow' }, label), input);
  const text = (type, key, ph, obj) => { const el = h('input', { type, class: 'search', style: { maxWidth: 'none', flex: 'none' }, placeholder: ph, autocomplete: 'off', value: obj[key] }); el.addEventListener('input', () => { obj[key] = el.value; }); return el; };

  function editor(title, f, onSave, onCancel, pwHint) {
    return h('div', { class: 'card' }, h('h2', { style: { fontSize: '16px' } }, title),
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
        field('Label', text('text', 'label', 'e.g. Default', f)),
        field('Password (max 8 characters)', text('password', 'password', pwHint, f)),
        field('Priority (1 = default)', text('number', 'priority', 'auto', f)),
        h('div', { style: { display: 'flex', gap: '8px' } },
          h('button', { class: 'btn primary', disabled: st.busy, onclick: onSave }, 'Save'),
          onCancel ? h('button', { class: 'btn', disabled: st.busy, onclick: onCancel }, 'Cancel') : null)));
  }

  function paint(err) {
    if (!st.rows) return;
    const rows = st.rows;
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      st.notice ? h('div', { class: st.notice.ok ? 'pill good' : 'banner', role: st.notice.ok ? 'status' : 'alert' }, st.notice.msg) : null,
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'VNC passwords'),
        h('div', { class: 'muted' }, 'The default password is tried first. If the asset rejects it, the next ones are tried in order. The password that worked last is remembered per asset.'))),
      h('div', { class: 'card' }, h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
        h('div', { class: 'tr th cols-vnc' }, h('div', null, 'Order'), h('div', null, 'Label'), h('div', null, 'Role'), h('div', null, 'Status'), h('div', null, '')),
        rows.map((r, i) => h('div', { class: 'tr cols-vnc' },
          h('div', { class: 'mono' }, String(r.priority)),
          h('div', { style: { fontWeight: '500' } }, r.label),
          h('div', null, r.is_default ? h('span', { class: 'pill good' }, 'Default') : r.enabled ? h('span', { class: 'pill info' }, `Fallback ${i}`) : h('span', { class: 'pill neutral' }, '—')),
          h('div', null, !r.readable ? h('span', { class: 'pill crit', title: 'The server secret changed; set the password again.' }, 'Unreadable') : r.enabled ? h('span', { class: 'pill good' }, 'Enabled') : h('span', { class: 'pill neutral' }, 'Disabled')),
          h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
            h('button', { class: 'btn', disabled: st.busy, onclick: () => run(() => api(`vnc/credentials/${r.id}`, { method: 'PUT', body: { enabled: !r.enabled } }), r.enabled ? 'Disabled.' : 'Enabled.') }, r.enabled ? 'Disable' : 'Enable'),
            h('button', { class: 'btn', disabled: st.busy, onclick: () => { st.editing = r.id; st.form = { label: r.label, password: '', priority: String(r.priority) }; paint(); } }, 'Edit'),
            h('button', { class: 'btn', disabled: st.busy, onclick: () => { if (confirm(`Delete VNC password "${r.label}"?`)) run(() => api(`vnc/credentials/${r.id}`, { method: 'DELETE' }), 'Deleted.'); } }, 'Delete')))),
        rows.length === 0 ? h('div', { class: 'empty' }, 'No VNC passwords yet. Add the one your TightVNC installations use.') : null))),
      st.editing !== null
        ? editor('Edit password', st.form, () => run(() => api(`vnc/credentials/${st.editing}`, { method: 'PUT', body: { label: st.form.label, password: st.form.password || undefined, priority: num(st.form.priority) } }), 'Saved.'),
          () => { st.editing = null; paint(); }, 'leave empty to keep')
        : editor('Add password', st.form, () => run(() => api('vnc/credentials', { method: 'POST', body: { label: st.form.label, password: st.form.password, priority: num(st.form.priority) } }), 'Added.'), null, 'VNC password'),
      h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Passwords are encrypted on the server and never shown again. VNC itself is not encrypted: keep port 5900 reachable only from your LAN/VPN.')].filter(Boolean));
  }
  return ld.stop;
}
