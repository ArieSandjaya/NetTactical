import { h, s } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, input, notice, btn } from '../forms.js';
import qrcode from '../vendor/qrcode.js';

// Own account security: enable or disable two-factor authentication (TOTP, works with Google/Microsoft Authenticator, Authy, 1Password...).
function qrSvg(text) {
  const q = qrcode(0, 'M'); q.addData(text); q.make();
  const n = q.getModuleCount(), pad = 4, size = n + pad * 2;
  const svg = s('svg', { viewBox: `0 0 ${size} ${size}`, width: 200, height: 200, role: 'img', 'aria-label': 'QR code for the authenticator app', style: { background: '#fff', borderRadius: '8px', border: '1px solid var(--line)' } });
  svg.append(s('rect', { width: size, height: size, fill: '#fff' }));
  let d = '';
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (q.isDark(r, c)) d += `M${c + pad} ${r + pad}h1v1h-1z`;
  svg.append(s('path', { d, fill: '#000' }));
  return svg;
}

export function securityView(root) {
  const st = { enabled: null, setup: null, notice: null, busy: false, f: { code: '', password: '' } };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  loader(body, (signal) => api('accounts/totp/status', { signal }), (d, err) => {
    if (d) st.enabled = d.enabled;
    paint(err);
  }, 0, 'Loading…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.notice = { ok: true, msg: okMsg }; } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const begin = () => run(async () => { st.setup = await api('accounts/totp/setup', { method: 'POST' }); st.f.code = ''; }, 'Scan the QR code, then enter the 6-digit code to finish.');
  const enable = () => run(async () => {
    await api('accounts/totp/enable', { method: 'POST', body: { secret: st.setup.secret, code: st.f.code.trim() } });
    st.enabled = true; st.setup = null; st.f.code = '';
  }, 'Two-factor authentication is on. You will need a code the next time you sign in.');
  const disable = () => run(async () => {
    await api('accounts/totp/disable', { method: 'POST', body: { password: st.f.password, code: st.f.code.trim() } });
    st.enabled = false; st.f.code = ''; st.f.password = '';
  }, 'Two-factor authentication is off.');

  function paint(err) {
    const head = h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Security'),
      h('p', { class: 'muted' }, 'Protect your sign-in with a code from an authenticator app.')));
    if (err) { body.replaceChildren(head, h('div', { class: 'banner', role: 'alert' }, err.message || String(err))); return; }
    if (st.enabled === null) return;
    let card;
    if (st.enabled) {
      card = h('section', { class: 'card stack', style: { gap: '14px' } },
        h('div', null, h('span', { class: 'pill good' }, 'Two-factor authentication is on')),
        h('p', { class: 'muted' }, 'To turn it off, confirm with your password and a current code.'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Password', input('password', st.f, 'password', '', { autocomplete: 'current-password' })),
          field('Current 6-digit code', input('text', st.f, 'code', '123456', { inputmode: 'numeric', maxlength: 8 }))),
        h('div', null, btn('Turn off 2FA', disable, { danger: true, disabled: st.busy })), notice(st.notice));
    } else if (st.setup) {
      const key = st.setup.secret.replace(/(.{4})/g, '$1 ').trim();
      card = h('section', { class: 'card stack', style: { gap: '16px' } },
        h('div', { class: 'stack' }, h('span', { class: 'eyebrow' }, '1. Scan with your authenticator app'), qrSvg(st.setup.uri)),
        h('div', { class: 'stack' }, h('span', { class: 'eyebrow' }, 'Or enter this key manually'), h('code', { class: 'mono', style: { fontSize: '15px', wordBreak: 'break-all' } }, key),
          h('span', { class: 'muted' }, 'Keep it private. It is shown only now and is not saved until you confirm.')),
        h('div', { class: 'stack' }, h('span', { class: 'eyebrow' }, '2. Enter the 6-digit code'),
          h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
            field('Code', input('text', st.f, 'code', '123456', { inputmode: 'numeric', maxlength: 8, autocomplete: 'one-time-code' }), '0 1 200px'),
            btn('Turn on 2FA', enable, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.setup = null; st.notice = null; paint(); }, { disabled: st.busy }))),
        notice(st.notice));
    } else {
      card = h('section', { class: 'card stack', style: { gap: '14px' } },
        h('div', null, h('span', { class: 'pill warn' }, 'Two-factor authentication is off')),
        h('p', { class: 'muted' }, 'Right now a password alone signs in to this account. Turn on 2FA so a code from your phone is also required.'),
        h('div', null, btn('Set up 2FA', begin, { primary: true, disabled: st.busy })), notice(st.notice));
    }
    body.replaceChildren(head, card);
  }
}
