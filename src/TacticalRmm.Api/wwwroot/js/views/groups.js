import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, select, notice, btn } from '../forms.js';

const why = (e) => (e.status === 403 ? 'You do not have permission for this.' : e.message);

// Groups of assets. A policy set on a group applies to all its members, so a policy is assigned once per group instead of once per asset.
export function groupsView(root) {
  const st = { groups: null, policies: null, assets: null, open: null, detail: null, notice: null, busy: false,
    form: { name: '', description: '', policy: '' }, edit: null, sel: new Set(), pick: new Set(), search: '' };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let alive = true;

  async function load() {
    try {
      const [groups, policies] = await Promise.all([api('asset-groups'), api('automation/policies').catch(() => null)]);
      if (!alive) return;
      st.groups = groups; st.policies = policies; paint();
    } catch (e) { if (alive) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, why(e))); }
  }
  const reload = async () => { st.groups = await api('asset-groups'); };
  async function act(fn, okMsg) {
    st.busy = true; st.notice = null; paint();
    try { await fn(); st.notice = okMsg ? { ok: true, msg: okMsg } : null; }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; paint();
  }

  const policyBody = (v) => (v === '' ? { clear_policy: true } : { policy: Number(v) });
  const create = () => {
    if (!st.form.name.trim()) { st.notice = { ok: false, msg: 'Enter a name for the group.' }; paint(); return; }
    return act(createNow, 'Group created.');
  };
  const createNow = async () => {
    const f = st.form;
    const payload = { name: f.name, description: f.description };
    if (f.policy !== '') payload.policy = Number(f.policy);
    const g = await api('asset-groups', { method: 'POST', body: payload });
    st.form = { name: '', description: '', policy: '' };
    await reload(); await openGroup(g.id, g);
  };

  async function openGroup(id, known) {
    if (st.open === id) { st.open = null; st.detail = null; paint(); return; }
    st.open = id; st.detail = known || null; st.edit = null; st.sel = new Set(); st.pick = new Set(); st.search = ''; paint();
    try { st.detail = await api(`asset-groups/${id}`); } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.edit = { name: st.detail?.name ?? '', description: st.detail?.description ?? '', policy: st.detail?.policy_id == null ? '' : String(st.detail.policy_id) };
    paint();
  }
  const save = (g) => act(async () => {
    const e = st.edit;
    st.detail = await api(`asset-groups/${g.id}`, { method: 'PUT', body: { name: e.name, description: e.description, ...policyBody(e.policy) } });
    await reload();
  }, 'Group saved.');
  const remove = (g) => {
    if (!confirm(`Delete the group "${g.name}"?\n\nIts ${g.members} asset${g.members === 1 ? '' : 's'} stay, but they no longer get the group's policy.`)) return;
    act(async () => { await api(`asset-groups/${g.id}`, { method: 'DELETE' }); st.open = null; st.detail = null; await reload(); }, 'Group deleted.');
  };
  const removeMembers = (g) => act(async () => {
    st.detail = await api(`asset-groups/${g.id}/members/remove`, { method: 'POST', body: { agent_ids: [...st.sel] } });
    st.sel = new Set(); await reload();
  }, 'Removed from the group.');
  const addMembers = (g) => {
    const moved = (st.assets || []).filter((a) => st.pick.has(a.agent_id) && a.group && a.group !== g.name).length;
    if (moved && !confirm(`${moved} of the chosen assets are in another group. An asset is in one group only, so they will move here. Continue?`)) return;
    act(async () => {
      st.detail = await api(`asset-groups/${g.id}/members`, { method: 'POST', body: { agent_ids: [...st.pick] } });
      st.pick = new Set(); st.assets = null; await reload();
    }, 'Added to the group.');
  };
  async function loadAssets() {
    try { st.assets = (await api('fleet/assets')).assets; }
    catch (e) { st.assets = []; st.notice = { ok: false, msg: why(e) }; }
    paint();
  }

  const tick = (set, key) => { const el = h('input', { type: 'checkbox' }); el.checked = set.has(key); el.addEventListener('change', () => { if (el.checked) set.add(key); else set.delete(key); paint(); }); return el; };
  const policySelect = (obj) => {
    const el = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none' }, 'aria-label': 'Policy' },
      [h('option', { value: '', selected: obj.policy === '' }, 'No policy'),
        ...st.policies.map((p) => h('option', { value: String(p.id), selected: String(p.id) === obj.policy }, p.active ? p.name : `${p.name} (inactive)`))]);
    el.addEventListener('change', () => { obj.policy = el.value; });
    return el;
  };

  function detailPanel(g) {
    const d = st.detail, e = st.edit;
    if (!d || !e) return h('div', { class: 'loading' }, 'Loading…');
    const memberIds = new Set(d.items.map((m) => m.agent_id));
    const free = (st.assets || []).filter((a) => !memberIds.has(a.agent_id) && (!st.search || `${a.hostname} ${a.client} ${a.site}`.toLowerCase().includes(st.search.toLowerCase()))).slice(0, 40);
    const search = h('input', { type: 'search', class: 'search', style: { maxWidth: 'none' }, placeholder: 'Search computers to add…', value: st.search, 'aria-label': 'Search computers' });
    search.addEventListener('input', () => { st.search = search.value; paint(); search.focus(); });
    return h('div', { class: 'stack', style: { gap: '14px' } },
      h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
        field('Name', input('text', e, 'name', ''), '2 1 200px'), field('Description', input('text', e, 'description', ''), '3 1 240px'),
        st.policies ? field('Policy for every member', policySelect(e), '2 1 220px') : null),
      h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap' } }, btn('Save', () => save(g), { primary: true, disabled: st.busy }), btn('Delete group', () => remove(g), { danger: true, disabled: st.busy })),
      h('strong', null, `Members (${d.members}${d.members > d.items.length ? `, you can see ${d.items.length}` : ''})`),
      d.items.length ? h('div', { class: 'scroll-x' }, h('table', { class: 'tbl' },
        h('thead', null, h('tr', null, ['', 'Computer', 'Client / site', 'Status'].map((t) => h('th', null, t)))),
        h('tbody', null, d.items.map((m) => h('tr', null, h('td', null, tick(st.sel, m.agent_id)), h('td', null, m.hostname), h('td', null, `${m.client} / ${m.site}`),
          h('td', null, h('span', { class: `pill ${m.online ? 'good' : 'neutral'}` }, m.online ? 'Online' : 'Offline'))))))) : h('div', { class: 'empty' }, 'No computers in this group yet.'),
      st.sel.size ? h('div', null, btn(`Remove ${st.sel.size} from the group`, () => removeMembers(g), { disabled: st.busy })) : null,
      h('strong', null, 'Add computers'),
      st.assets ? h('div', { class: 'stack', style: { gap: '6px' } }, search,
        h('div', { class: 'stack', style: { gap: '2px', maxHeight: '240px', overflow: 'auto' } }, free.map((a) => h('label', { class: 'check' }, tick(st.pick, a.agent_id),
          h('span', null, `${a.hostname} · ${a.client} / ${a.site}${a.group ? ` · now in "${a.group}"` : ''}`)))),
        free.length === 40 ? h('span', { class: 'muted' }, 'Showing the first 40 matches. Type to narrow down.') : null,
        st.pick.size ? h('div', null, btn(`Add ${st.pick.size} to the group`, () => addMembers(g), { primary: true, disabled: st.busy })) : null)
        : h('div', null, btn('Choose computers…', loadAssets)));
  }

  function row(g) {
    const open = st.open === g.id;
    return h('section', { class: 'card stack', style: { gap: '12px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } },
        h('div', { class: 'stack', style: { gap: '2px' } }, h('strong', null, g.name), g.description ? h('span', { class: 'muted' }, g.description) : null),
        h('div', { style: { display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' } },
          h('span', { class: 'pill neutral' }, `${g.members} asset${g.members === 1 ? '' : 's'}`),
          h('span', { class: `pill ${g.policy_name ? 'good' : 'neutral'}` }, g.policy_name ? `Policy: ${g.policy_name}` : 'No policy'),
          btn(open ? 'Close' : 'Open', () => openGroup(g.id)))),
      open ? detailPanel(g) : null);
  }

  function paint() {
    if (!st.groups) return;
    const f = st.form;
    body.replaceChildren(...[
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Groups'),
        h('p', { class: 'muted' }, 'Put assets from any client or site in a group and give the group a policy once. Every member gets it. An asset is in one group; its own policy still wins, and the group\'s policy wins over its site and client.'))),
      h('section', { class: 'card stack', style: { gap: '12px' } },
        h('strong', null, 'New group'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'flex-end' } },
          field('Name', input('text', f, 'name', 'Reception PCs'), '2 1 200px'), field('Description (optional)', input('text', f, 'description', ''), '3 1 240px'),
          st.policies ? field('Policy (optional)', policySelect(f), '2 1 220px') : null,
          btn(st.busy ? 'Creating…' : 'Create group', create, { primary: true, disabled: st.busy }))),
      notice(st.notice),
      ...(st.groups.length ? st.groups.map(row) : [h('div', { class: 'empty' }, 'No groups yet.')]),
    ].filter(Boolean));
  }

  load();
  return () => { alive = false; };
}
