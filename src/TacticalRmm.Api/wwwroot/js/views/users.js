import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, input, check, select, notice, btn } from '../forms.js';

export function usersView(root) {
  const st = { users: null, roles: [], notice: null, busy: false, mode: null, form: {} };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  const ld = loader(body, async (signal) => ({ users: await api('accounts/users', { signal }), roles: await api('accounts/roles', { signal }) }),
    (d, err) => { if (d) { st.users = d.users; st.roles = d.roles; } paint(err); }, 0, 'Loading users…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.users = await api('accounts/users'); st.mode = null; st.notice = { ok: true, msg: okMsg }; }
    catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const roleOpts = () => [['', '— none —'], ...st.roles.map((r) => [r.id, r.name])];
  const open = (mode, form) => { st.mode = mode; st.form = form; st.notice = null; paint(); };

  function editor() {
    const f = st.form, m = st.mode;
    if (!m) return null;
    const title = m === 'new' ? 'Add user' : m === 'edit' ? `Edit ${f.username}` : `Reset password for ${f.username}`;
    const fields = m === 'pw' ? [field('New password', input('password', f, 'password', 'At least 12 characters'))]
      : m === 'new' ? [field('Username', input('text', f, 'username', 'username')), field('Email', input('email', f, 'email', 'name@example.com')), field('Password', input('password', f, 'password', 'At least 12 characters')),
        field('Role', select(roleOpts(), f, 'roleId'))]
      : [field('Email', input('email', f, 'email', 'name@example.com')), field('Role', select(roleOpts(), f, 'roleId'))];
    const flags = m === 'pw' ? [] : [check('Superuser (all permissions)', f, 'isSuperuser'), check('Block dashboard login (API/agent use only)', f, 'block'), m === 'edit' ? check('Active', f, 'isActive') : null].filter(Boolean);
    const save = () => {
      if (m === 'pw') return run(() => api(`accounts/users/${f.id}/reset-password`, { method: 'POST', body: { password: f.password } }), 'Password reset.');
      if (m === 'new') return run(() => api('accounts/users', { method: 'POST', body: { username: f.username, email: f.email || undefined, password: f.password, role_id: f.roleId ? +f.roleId : undefined, is_superuser: !!f.isSuperuser, block_dashboard_login: !!f.block } }), 'User created.');
      return run(() => api(`accounts/users/${f.id}`, { method: 'PUT', body: { email: f.email || '', role_id: f.roleId ? +f.roleId : undefined, clear_role: !f.roleId, is_superuser: !!f.isSuperuser, block_dashboard_login: !!f.block, is_active: !!f.isActive } }), 'Saved.');
    };
    return h('section', { class: 'card' }, h('h2', { style: { fontSize: '16px' } }, title),
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } }, fields),
      flags.length ? h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '16px', marginTop: '12px' } }, flags) : null,
      h('div', { style: { display: 'flex', gap: '8px', marginTop: '14px' } }, btn('Save', save, { primary: true, disabled: st.busy }), btn('Cancel', () => { st.mode = null; paint(); })));
  }

  function paint(err) {
    if (!st.users) return;
    const edit = (u) => open('edit', { id: u.id, username: u.username, email: u.email || '', roleId: u.role_id ?? '', isSuperuser: u.is_superuser, block: u.block_dashboard_login, isActive: u.is_active });
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      notice(st.notice),
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Users'), h('div', { class: 'muted' }, `${st.users.length} users`)),
        btn('Add user', () => open('new', { username: '', email: '', password: '', roleId: '', isSuperuser: false, block: false }), { primary: true, disabled: st.busy })),
      h('div', { class: 'card' }, h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
        h('div', { class: 'tr th cols-user' }, h('div', null, 'User'), h('div', null, 'Role'), h('div', null, 'Flags'), h('div', null, 'Last login'), h('div', null, '')),
        st.users.map((u) => h('div', { class: 'tr cols-user' },
          h('div', { class: 'stack', style: { gap: '2px' } }, h('span', { style: { fontWeight: '500' } }, u.username), h('span', { class: 'muted', style: { fontSize: '12px' } }, u.email || '')),
          h('div', null, u.is_superuser ? h('span', { class: 'pill info' }, 'All permissions') : u.role_name ? u.role_name : h('span', { class: 'muted' }, 'No role')),
          h('div', { style: { display: 'flex', gap: '4px', flexWrap: 'wrap' } },
            u.is_superuser ? h('span', { class: 'pill info' }, 'Superuser') : null, !u.is_active ? h('span', { class: 'pill crit' }, 'Inactive') : null,
            u.block_dashboard_login ? h('span', { class: 'pill warn' }, 'Dashboard blocked') : null, u.totp_enabled ? h('span', { class: 'pill good' }, '2FA') : null),
          h('div', { class: 'muted mono', style: { fontSize: '12px' } }, u.last_login_ip || '—'),
          h('div', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap', justifyContent: 'flex-end' } },
            btn('Edit', () => edit(u), { disabled: st.busy }),
            btn('Password', () => open('pw', { id: u.id, username: u.username, password: '' }), { disabled: st.busy }),
            u.totp_enabled ? btn('Reset 2FA', () => { if (confirm(`Reset two-factor authentication for ${u.username}?`)) run(() => api(`accounts/users/${u.id}/reset-totp`, { method: 'POST' }), '2FA reset.'); }, { disabled: st.busy }) : null,
            btn('Delete', () => { if (confirm(`Delete user ${u.username}?`)) run(() => api(`accounts/users/${u.id}`, { method: 'DELETE' }), 'User deleted.'); }, { disabled: st.busy })))),
        st.users.length === 0 ? h('div', { class: 'empty' }, 'No users.') : null))),
      editor()].filter(Boolean));
  }
  return ld.stop;
}
