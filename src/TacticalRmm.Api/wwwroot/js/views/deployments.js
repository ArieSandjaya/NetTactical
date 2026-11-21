import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, check, select, input, notice, btn } from '../forms.js';

const HOURS = [[1, '1 hour'], [24, '24 hours'], [72, '3 days'], [168, '7 days'], [720, '30 days'], [2160, '90 days'], [8760, '1 year']];
const why = (e) => (e.status === 403 ? 'You do not have permission for deployments.' : e.message);

// Deployment links: one link per client/site that installs the agent on whoever runs it, until it expires or is used up.
export function deploymentsView(root) {
  const st = { clients: null, list: null, notice: null, busy: false, copied: null,
    form: { client: '', site: '', agent_type: 'workstation', arch: 'amd64', expires_hours: '168', max_uses: '', note: '', rdp: false, ping: false, power: false, api: '', nats: '' } };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);
  let alive = true;

  const sitesOf = () => st.clients.find((c) => String(c.id) === String(st.form.client))?.sites ?? [];

  async function load() {
    try {
      const [clients, list] = await Promise.all([api('clients'), api('deployments').catch((e) => { if (e.status === 403) return []; throw e; })]);
      if (!alive) return;
      st.clients = clients; st.list = list;
      if (!st.form.client && clients.length) { st.form.client = String(clients[0].id); st.form.site = String(clients[0].sites[0]?.id ?? ''); }
      paint();
    } catch (e) { if (alive) body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, why(e))); }
  }

  async function create() {
    const f = st.form;
    st.busy = true; st.notice = null; paint();
    try {
      const payload = { client_id: Number(f.client), site_id: Number(f.site), agent_type: f.agent_type, arch: f.arch, expires_hours: Number(f.expires_hours), rdp: f.rdp, ping: f.ping, power: f.power };
      if (f.max_uses) payload.max_uses = Number(f.max_uses);
      if (f.note.trim()) payload.note = f.note.trim();
      if (f.api.trim()) payload.api = f.api.trim();
      if (f.nats) payload.nats_port = Number(f.nats);
      await api('deployments', { method: 'POST', body: payload });
      st.notice = { ok: true, msg: 'Link created.' };
      st.list = await api('deployments');
    } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; paint();
  }

  async function remove(d) {
    if (!confirm(`Delete the link for ${d.client_name} / ${d.site_name}?\n\nThe link stops working immediately. Agents already installed are not affected.`)) return;
    try { await api(`deployments/${d.id}`, { method: 'DELETE' }); st.list = await api('deployments'); st.notice = { ok: true, msg: 'Link deleted.' }; }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    paint();
  }

  async function copy(key, text, node) {
    try { await navigator.clipboard.writeText(text); st.copied = key; }
    catch { const r = document.createRange(); r.selectNodeContents(node); const s = getSelection(); s.removeAllRanges(); s.addRange(r); st.copied = `${key}:select`; }
    paint();
  }

  function card(d) {
    const pre = h('pre', { class: 'act-out', tabindex: '0' }, d.command);
    const label = st.copied === `c${d.id}` ? 'Copied' : st.copied === `c${d.id}:select` ? 'Selected, press Ctrl+C' : 'Copy command';
    const used = d.max_uses ? `${d.uses} of ${d.max_uses} used` : `${d.uses} used`;
    const dead = d.expired || (d.max_uses && d.uses >= d.max_uses);
    return h('section', { class: 'card stack', style: { gap: '10px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } },
        h('strong', null, `${d.client_name} / ${d.site_name} · ${d.agent_type} · ${d.arch === 'amd64' ? '64-bit' : '32-bit'}`),
        h('span', { class: `pill ${dead ? 'crit' : 'good'}` }, d.expired ? 'Expired' : dead ? 'Used up' : 'Active')),
      h('div', { class: 'muted' }, `${d.expired ? 'Expired' : 'Expires'} ${new Date(d.expires_at).toLocaleString()} · ${used} · by ${d.created_by || '?'}${d.note ? ` · ${d.note}` : ''}`),
      dead ? null : h('div', { class: 'stack', style: { gap: '6px' } }, h('span', { class: 'eyebrow' }, 'Run in PowerShell as Administrator on the computer'), pre,
        h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap' } }, btn(label, () => copy(`c${d.id}`, d.command, pre)),
          h('a', { href: d.link, target: '_blank', rel: 'noopener noreferrer', class: 'btn' }, 'Open the script'))),
      h('div', null, btn('Delete', () => remove(d), { danger: true })));
  }

  function paint() {
    if (!st.clients) return;
    const f = st.form;
    if (!st.clients.length) { body.replaceChildren(h('div', { class: 'page-head' }, h('h1', null, 'Deployments')), h('div', { class: 'empty' }, 'Create a client and a site first (Clients & Sites).')); return; }
    const clientSel = select(st.clients.map((c) => [c.id, c.name]), f, 'client');
    clientSel.addEventListener('change', () => { f.site = String(sitesOf()[0]?.id ?? ''); paint(); });
    const natsSel = h('select', { class: 'search', style: { maxWidth: 'none', flex: 'none' }, 'aria-label': 'How agents reach NATS' },
      [['', 'Default: WebSocket on port 443 (needs a reverse proxy)'], ['4222', 'Direct to NATS on port 4222']].map(([v, t]) => h('option', { value: v, selected: f.nats === v }, t)));
    natsSel.addEventListener('change', () => { f.nats = natsSel.value; });
    body.replaceChildren(...[
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Deployments'),
        h('p', { class: 'muted' }, 'A link per client/site. Whoever runs its command as Administrator installs the agent there, until the link expires or is used up. Each use creates a fresh short-lived install token.'))),
      h('section', { class: 'card stack', style: { gap: '14px' } },
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Client', clientSel), field('Site', select(sitesOf().map((s) => [s.id, s.name]), f, 'site')),
          field('Type', select([['workstation', 'Workstation'], ['server', 'Server']], f, 'agent_type'), '1 1 140px'),
          field('Architecture', select([['amd64', '64-bit'], ['386', '32-bit']], f, 'arch'), '1 1 120px'),
          field('Link valid for', select(HOURS, f, 'expires_hours'), '1 1 140px'),
          field('Max computers (optional)', input('number', f, 'max_uses', 'no limit'), '1 1 150px')),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '16px' } }, check('Enable RDP', f, 'rdp'), check('Enable ping rule', f, 'ping'), check('Disable sleep / hibernate', f, 'power')),
        field('Note (optional)', input('text', f, 'note', 'for example: Floor 3 laptops')),
        field('Server URL agents will use (optional, defaults to this page\'s address)', input('text', f, 'api', 'https://rmm.example.com')),
        field('How agents reach NATS', natsSel),
        h('div', { class: 'banner', role: 'note' }, 'Anyone who has the link can enroll computers in this client/site until it expires, so share it only with whoever installs. Delete it when the job is done.'),
        h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } },
          btn(st.busy ? 'Creating…' : 'Create link', create, { primary: true, disabled: st.busy || !f.site }), notice(st.notice))),
      ...(st.list.length ? st.list.map(card) : [h('div', { class: 'empty' }, 'No deployment links yet.')]),
    ].filter(Boolean));
  }

  load();
  return () => { alive = false; };
}
