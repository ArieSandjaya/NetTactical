import { h } from '../dom.js';
import { api } from '../api.js';
import { loader, gridTable, pageHead } from '../ui.js';
import { field, input, check, select, notice, btn } from '../forms.js';
import { ago } from '../format.js';

const SEV = [['info', 'Info and above'], ['warning', 'Warning and above'], ['error', 'Critical only']];
const KINDS = [['email', 'Email (SMTP)'], ['webhook', 'Webhook (Slack, Teams, custom)']];
const FORMATS = [['json', 'JSON (custom receiver)'], ['slack', 'Slack / Mattermost / Teams text']];
const blank = () => ({ id: null, kind: 'email', name: '', enabled: true, min_severity: 'warning', notify_resolved: true, only_flagged: false, host: '', port: '587', start_tls: true, username: '',
  from_address: '', recipients: '', url: '', format: 'json', secret: '', clear_secret: false, has_secret: false });
const fromRow = (c) => ({ ...blank(), ...c, port: String(c.port ?? 587), host: c.host || '', username: c.username || '', from_address: c.from_address || '', recipients: c.recipients || '', url: c.url || '', format: c.format || 'json', secret: '', clear_secret: false });

export function notificationsView(root) {
  const st = { rows: null, notice: null, busy: false, edit: null, tests: {} };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  const ld = loader(body, (signal) => api('notifications/channels', { signal }), (d, err) => { if (d) st.rows = d; paint(err); }, 0, 'Loading notification channels…');

  async function run(fn, okMsg, after) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.rows = await api('notifications/channels'); after?.(); st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  function payload() {
    const f = st.edit, p = { name: f.name.trim(), enabled: f.enabled, min_severity: f.min_severity, notify_resolved: f.notify_resolved, only_flagged: f.only_flagged };
    if (!f.id) p.kind = f.kind;
    if (f.kind === 'email') Object.assign(p, { host: f.host.trim(), port: Number(f.port) || 0, start_tls: f.start_tls, username: f.username.trim(), from_address: f.from_address.trim(), recipients: f.recipients });
    else Object.assign(p, { url: f.url.trim(), format: f.format });
    if (f.secret) p.secret = f.secret;
    if (f.clear_secret) p.clear_secret = true;
    return p;
  }
  const save = () => { const f = st.edit; run(() => (f.id ? api(`notifications/channels/${f.id}`, { method: 'PUT', body: payload() }) : api('notifications/channels', { method: 'POST', body: payload() })),
    f.id ? 'Channel saved.' : 'Channel added. Use Send test to check it.', () => { st.edit = null; }); };
  async function test(c) {
    st.tests[c.id] = { busy: true }; paint();
    try { st.tests[c.id] = await api(`notifications/channels/${c.id}/test`, { method: 'POST' }); st.rows = await api('notifications/channels'); }
    catch (e) { st.tests[c.id] = { ok: false, error: e.message }; }
    paint();
  }

  function form() {
    const f = st.edit, email = f.kind === 'email';
    const kindSel = select(KINDS, f, 'kind'); kindSel.disabled = !!f.id; kindSel.addEventListener('change', () => paint());
    const rec = h('textarea', { class: 'search', rows: 2, style: { maxWidth: 'none', flex: 'none', width: '100%' }, placeholder: 'ops@example.com, noc@example.com' });
    rec.value = f.recipients; rec.addEventListener('input', () => { f.recipients = rec.value; });
    const secretLabel = email ? 'SMTP password' : 'Signing secret (optional)';
    return h('section', { class: 'card stack', style: { gap: '14px' } }, h('h2', { style: { fontSize: '16px' } }, f.id ? `Edit "${f.name}"` : 'New channel'),
      h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Name', input('text', f, 'name', 'e.g. NOC mailbox'), '2 1 220px'), field('Type', kindSel, '1 1 220px'), field('Send', select(SEV, f, 'min_severity'), '1 1 180px')),
      email ? h('div', { class: 'stack', style: { gap: '12px' } },
        h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('SMTP server', input('text', f, 'host', 'smtp.example.com'), '2 1 220px'), field('Port', input('number', f, 'port', '587', { min: 1, max: 65535 }), '1 1 100px')),
        check('Use STARTTLS (recommended, port 587). Implicit SSL on port 465 is not supported.', f, 'start_tls'),
        h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Username (blank if none)', input('text', f, 'username', '', { autocomplete: 'off' }), '1 1 200px'),
          field(secretLabel + (f.has_secret ? ' (saved, leave blank to keep)' : ''), input('password', f, 'secret', '', { autocomplete: 'new-password' }), '1 1 200px')),
        field('From address', input('text', f, 'from_address', 'rmm@example.com')), field('Recipients', rec))
        : h('div', { class: 'stack', style: { gap: '12px' } }, field('Webhook URL', input('text', f, 'url', 'https://hooks.example.com/…')),
          h('div', { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } }, field('Payload', select(FORMATS, f, 'format'), '1 1 240px'),
            field(secretLabel + (f.has_secret ? ' (saved, leave blank to keep)' : ''), input('password', f, 'secret', '', { autocomplete: 'new-password' }), '1 1 240px')),
          h('p', { class: 'muted' }, 'With a secret, every request carries X-NetTactical-Signature: sha256=<HMAC of the body> so the receiver can verify it. Addresses on this server (localhost) and link-local addresses are blocked.')),
      f.has_secret ? check(`Remove the saved ${email ? 'password' : 'secret'}`, f, 'clear_secret') : null,
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap' } }, check('Enabled', f, 'enabled'), check('Also notify when an alert resolves', f, 'notify_resolved'),
        check('Only for items with the email alert option turned on', f, 'only_flagged')),
      h('div', { style: { display: 'flex', gap: '8px' } }, btn(f.id ? 'Save changes' : 'Add channel', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.edit = null; st.notice = null; paint(); })));
  }

  const statusCell = (c) => {
    const t = st.tests[c.id];
    if (t?.busy) return h('span', { class: 'pill neutral' }, 'Sending…');
    if (c.last_ok == null) return h('span', { class: 'pill neutral' }, 'Never used');
    return h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { class: `pill ${c.last_ok ? 'good' : 'crit'}`, style: { alignSelf: 'flex-start' } }, c.last_ok ? 'Last send OK' : 'Last send failed'),
      h('span', { class: 'muted' }, ago(c.last_attempt)), c.last_error ? h('span', { class: 'muted', style: { overflowWrap: 'anywhere' } }, c.last_error) : null);
  };

  function paint(err) {
    if (!st.rows) return;
    body.replaceChildren(...[
      pageHead('Notifications', 'Email and webhook delivery for new and resolved alerts', btn('New channel', () => { st.edit = blank(); st.notice = null; paint(); }, { primary: true, disabled: !!st.edit })),
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null, notice(st.notice),
      st.edit ? form() : null,
      h('section', { class: 'card' }, gridTable('cols-notif', ['Channel', 'Sends', 'Status', ''], st.rows.map((c) => h('div', { class: 'tr cols-notif' },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '600' } }, c.name),
          h('span', { class: 'muted', style: { overflowWrap: 'anywhere' } }, c.kind === 'email' ? `Email · ${c.host}:${c.port} → ${c.recipients}` : `Webhook · ${c.url}`),
          c.enabled ? null : h('span', { class: 'pill neutral', style: { alignSelf: 'flex-start' } }, 'Disabled')),
        h('div', { class: 'muted' }, `${SEV.find(([k]) => k === c.min_severity)?.[1] || c.min_severity}${c.notify_resolved ? ' + resolved' : ''}${c.only_flagged ? ' · flagged only' : ''}`),
        statusCell(c),
        h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end', flexWrap: 'wrap' } },
          btn('Send test', () => test(c), { disabled: st.busy || st.tests[c.id]?.busy }), btn('Edit', () => { st.edit = fromRow(c); st.notice = null; paint(); document.querySelector('h1')?.scrollIntoView(); }, { disabled: st.busy }),
          btn('Delete', () => { if (confirm(`Delete channel "${c.name}"?`)) run(() => api(`notifications/channels/${c.id}`, { method: 'DELETE' }), 'Channel deleted.'); }, { disabled: st.busy, danger: true })))),
        'No channels yet. Add an email or webhook channel to be told about alerts outside the dashboard.'),
        Object.entries(st.tests).filter(([, r]) => r && !r.busy).map(([id, r]) => st.rows.some((c) => String(c.id) === id)
          ? h('div', { class: r.ok ? 'pill good' : 'banner', role: 'status', style: { marginTop: '10px', display: 'block' } }, `${st.rows.find((c) => String(c.id) === id).name}: ${r.ok ? 'test notification sent.' : `test failed: ${r.error}`}`) : null))].filter(Boolean));
  }
  return ld.stop;
}
