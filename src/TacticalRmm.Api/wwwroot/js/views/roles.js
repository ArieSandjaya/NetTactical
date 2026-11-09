import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, input, check, notice, btn } from '../forms.js';

const label = (p) => p.replace(/^can_/, '').replaceAll('_', ' ');

export function rolesView(root) {
  const st = { roles: null, perms: [], clients: [], notice: null, busy: false, form: null };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  const ld = loader(body, async (signal) => ({ roles: await api('accounts/roles', { signal }), perms: await api('accounts/permissions', { signal }), clients: await api('clients', { signal }).catch(() => []) }),
    (d, err) => { if (d) Object.assign(st, d); paint(err); }, 0, 'Loading roles…');

  async function run(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.roles = await api('accounts/roles'); st.form = null; st.notice = { ok: true, msg: okMsg }; }
    catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }
  const blank = () => ({ id: null, name: '', isSuper: false, perms: new Set(), clients: new Set(), sites: new Set() });
  const edit = (r) => { st.form = { id: r.id, name: r.name, isSuper: r.is_superuser, perms: new Set(r.permissions), clients: new Set(r.can_view_clients), sites: new Set(r.can_view_sites) }; st.notice = null; paint(); };
  const toggle = (set, v, on) => (on ? set.add(v) : set.delete(v));

  function box(text, set, v, disabled = false) {
    const el = h('input', { type: 'checkbox', disabled }); el.checked = set.has(v);
    el.addEventListener('change', () => toggle(set, v, el.checked));
    return h('label', { class: 'check' }, el, h('span', null, text));
  }

  function editor() {
    const f = st.form; if (!f) return null;
    // Built at click time: the form object is edited in place while the user types.
    const payload = () => ({ name: f.name, is_superuser: f.isSuper, permissions: [...f.perms], can_view_clients: [...f.clients], can_view_sites: [...f.sites] });
    return h('section', { class: 'card' }, h('h2', { style: { fontSize: '16px' } }, f.id ? `Edit role "${f.name}"` : 'Add role'),
      h('div', { style: { display: 'flex', gap: '16px', flexWrap: 'wrap', alignItems: 'flex-end' } },
        field('Name', input('text', f, 'name', 'e.g. Helpdesk'), '1 1 260px'), check('Superuser (all permissions, all clients)', f, 'isSuper', paint)),
      h('div', { class: 'eyebrow', style: { marginTop: '16px' } }, 'Permissions'),
      h('div', { class: 'perm-grid' }, st.perms.map((p) => box(label(p), f.perms, p, f.isSuper))),
      h('div', { class: 'eyebrow', style: { marginTop: '16px' } }, 'Limit to these clients and sites (leave all unchecked for no limit)'),
      h('div', { class: 'perm-grid' }, st.clients.map((c) => h('div', { class: 'stack', style: { gap: '4px' } }, box(c.name, f.clients, c.id, f.isSuper),
        c.sites.map((s) => h('div', { style: { marginLeft: '22px' } }, box(s.name, f.sites, s.id, f.isSuper)))))),
      h('div', { style: { display: 'flex', gap: '8px', marginTop: '14px' } },
        btn('Save', () => run(() => api(f.id ? `accounts/roles/${f.id}` : 'accounts/roles', { method: f.id ? 'PUT' : 'POST', body: payload() }), 'Saved.'), { primary: true, disabled: st.busy }),
        btn('Cancel', () => { st.form = null; paint(); })));
  }

  function paint(err) {
    if (!st.roles) return;
    body.replaceChildren(...[
      err ? h('div', { class: 'banner', role: 'alert' }, `Refresh failed: ${err}`) : null,
      notice(st.notice),
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Roles'), h('div', { class: 'muted' }, 'A role is a set of permissions, optionally limited to some clients or sites.')),
        btn('Add role', () => { st.form = blank(); st.notice = null; paint(); }, { primary: true, disabled: st.busy })),
      h('div', { class: 'card' }, h('div', { class: 'scroll-x' }, h('div', { class: 'grid-table' },
        h('div', { class: 'tr th cols-role' }, h('div', null, 'Role'), h('div', null, 'Permissions'), h('div', null, 'Scope'), h('div', null, '')),
        st.roles.map((r) => h('div', { class: 'tr cols-role' },
          h('div', { style: { fontWeight: '500' } }, r.name),
          h('div', null, r.is_superuser ? h('span', { class: 'pill info' }, 'All permissions') : h('span', { class: 'muted' }, `${r.permissions.length} of ${st.perms.length}`)),
          h('div', { class: 'muted' }, r.can_view_clients.length + r.can_view_sites.length === 0 ? 'All clients' : `${r.can_view_clients.length} clients, ${r.can_view_sites.length} sites`),
          h('div', { style: { display: 'flex', gap: '6px', justifyContent: 'flex-end' } },
            btn('Edit', () => edit(r), { disabled: st.busy }),
            btn('Delete', () => { if (confirm(`Delete role "${r.name}"? Users with this role lose its permissions.`)) run(() => api(`accounts/roles/${r.id}`, { method: 'DELETE' }), 'Role deleted.'); }, { disabled: st.busy })))),
        st.roles.length === 0 ? h('div', { class: 'empty' }, 'No roles yet.') : null))),
      editor()].filter(Boolean));
  }
  return ld.stop;
}
