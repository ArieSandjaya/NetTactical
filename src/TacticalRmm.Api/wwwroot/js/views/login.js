import { h } from '../dom.js';
import { login, session } from '../api.js';

export function loginView(root) {
  let needTotp = false;
  const err = h('div', { class: 'error', role: 'alert', hidden: true });
  const user = h('input', { id: 'u', name: 'username', autocomplete: 'username', required: true });
  const pass = h('input', { id: 'p', name: 'password', type: 'password', autocomplete: 'current-password', required: true });
  const totp = h('input', { id: 't', name: 'totp', inputmode: 'numeric', autocomplete: 'one-time-code', maxlength: 8 });
  const totpRow = h('label', { hidden: true }, 'Kode 2FA', totp);
  const btn = h('button', { class: 'btn primary', type: 'submit' }, 'Masuk');

  const form = h('form', {
    onsubmit: async (e) => {
      e.preventDefault();
      err.hidden = true; btn.disabled = true;
      try {
        const r = await login(user.value.trim(), pass.value, needTotp ? totp.value.trim() : undefined);
        if (r?.totp) { needTotp = true; totpRow.hidden = false; totp.focus(); return; }
        if (!r?.token) throw new Error('Respons login tidak valid.');
        session.set(r.token);
        location.hash = '#/';
      } catch (ex) { err.textContent = ex.message || 'Gagal masuk.'; err.hidden = false; }
      finally { btn.disabled = false; }
    },
  },
    h('h1', null, 'NetTactical'),
    h('p', { class: 'muted', style: { margin: '0' } }, 'Masuk untuk melihat kesehatan armada.'),
    err,
    h('label', null, 'Nama pengguna', user),
    h('label', null, 'Kata sandi', pass),
    totpRow, btn);

  root.replaceChildren(h('main', { class: 'login' }, form));
  user.focus();
  return () => {};
}
