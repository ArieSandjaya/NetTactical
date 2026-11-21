import { h } from '../dom.js';
import { api } from '../api.js';
import { field, input, notice, btn } from '../forms.js';

const TAGS = ['agent.hostname', 'agent.agent_id', 'agent.public_ip', 'agent.logged_in_user', 'agent.description', 'client.name', 'client.id', 'site.name', 'site.id'];
const why = (e) => (e.status === 403 ? 'You do not have permission for URL actions.' : e.message);

// Small menu for an asset: pick an action, get its link with the asset's values filled in, click it. Created once per asset page.
export function urlActionsMenu(agentId) {
  const el = h('span', { style: { display: 'inline-flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' } });
  let list = [];
  let out = null;
  const paint = () => {
    if (!list.length) { el.replaceChildren(); return; }
    const sel = h('select', { class: 'search', style: { maxWidth: '220px', flex: 'none' }, 'aria-label': 'URL actions' },
      h('option', { value: '' }, 'URL actions…'), ...list.map((a) => h('option', { value: a.id, title: a.description || '' }, a.name)));
    sel.addEventListener('change', async () => {
      const id = sel.value; if (!id) return;
      out = { msg: 'Preparing…' }; paint();
      try { const r = await api(`urlactions/${id}/run`, { method: 'POST', body: { agent_id: agentId } }); out = { url: r.url, name: list.find((a) => String(a.id) === id)?.name }; }
      catch (e) { out = { err: why(e) }; }
      paint();
    });
    el.replaceChildren(sel,
      out?.url ? h('a', { href: out.url, target: '_blank', rel: 'noopener noreferrer', class: 'btn' }, `Open ${out.name}`) : null,
      out?.err ? h('span', { class: 'pill crit', role: 'alert' }, out.err) : out?.msg ? h('span', { class: 'muted' }, out.msg) : null);
  };
  api('urlactions').then((l) => { list = l; paint(); }).catch(() => {}); // no permission or none defined: the menu stays hidden
  return el;
}

// Admin page: define the links.
export function urlActionsView(root) {
  const st = { list: null, form: { id: null, name: '', description: '', pattern: '' }, notice: null, busy: false };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let alive = true;

  async function load() {
    try { st.list = await api('urlactions'); if (alive) paint(); }
    catch (e) { if (alive) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, why(e))); }
  }
  async function save() {
    const f = st.form; st.busy = true; st.notice = null; paint();
    try {
      const payload = { name: f.name, description: f.description, pattern: f.pattern };
      await api(f.id ? `urlactions/${f.id}` : 'urlactions', { method: f.id ? 'PUT' : 'POST', body: payload });
      st.form = { id: null, name: '', description: '', pattern: '' }; st.notice = { ok: true, msg: 'Saved.' }; st.list = await api('urlactions');
    } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; if (alive) paint();
  }
  async function remove(a) {
    if (!confirm(`Delete the URL action ${a.name}?`)) return;
    try { await api(`urlactions/${a.id}`, { method: 'DELETE' }); st.list = await api('urlactions'); st.notice = { ok: true, msg: 'Deleted.' }; }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    paint();
  }

  function paint() {
    if (!st.list) return;
    const f = st.form;
    body.replaceChildren(
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'URL actions'),
        h('p', { class: 'muted' }, 'Saved links for an asset page, for example a ticket search or a documentation page. Placeholders are filled with the asset\'s values (URL-encoded) when someone opens the link.'))),
      h('section', { class: 'card stack', style: { gap: '12px' } },
        h('strong', null, f.id ? 'Change action' : 'New action'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } }, field('Name', input('text', f, 'name', 'Search tickets')), field('Description (optional)', input('text', f, 'description', ''))),
        field('URL', input('text', f, 'pattern', 'https://tickets.example.com/search?q={{agent.hostname}}')),
        h('div', { class: 'muted' }, `Placeholders: ${TAGS.map((t) => `{{${t}}}`).join('  ')}`),
        h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } },
          btn(st.busy ? 'Saving…' : 'Save', save, { primary: true, disabled: st.busy }),
          f.id ? btn('Cancel', () => { st.form = { id: null, name: '', description: '', pattern: '' }; paint(); }) : null, notice(st.notice))),
      ...(st.list.length ? st.list.map((a) => h('section', { class: 'card stack', style: { gap: '6px' } },
        h('strong', null, a.name), a.description ? h('div', { class: 'muted' }, a.description) : null, h('pre', { class: 'act-out', tabindex: '0' }, a.pattern),
        h('div', { style: { display: 'flex', gap: '8px' } }, btn('Edit', () => { st.form = { id: a.id, name: a.name, description: a.description || '', pattern: a.pattern }; paint(); }), btn('Delete', () => remove(a), { danger: true }))))
        : [h('div', { class: 'empty' }, 'No URL actions yet.')]));
  }
  load();
  return () => { alive = false; };
}
