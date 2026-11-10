import { h } from '../dom.js';
import { api } from '../api.js';
import { loader } from '../ui.js';
import { field, check, select, notice, btn } from '../forms.js';

// Agent installer generator: pick client/site, get a time-limited token plus copy-ready install commands.
const HOURS = [[1, '1 hour'], [8, '8 hours'], [24, '24 hours'], [72, '3 days'], [168, '7 days'], [720, '30 days']];

export function installerView(root) {
  const st = { clients: null, notice: null, busy: false, result: null, copied: null,
    form: { client: '', site: '', agent_type: 'workstation', arch: 'amd64', expires_hours: '24', rdp: false, ping: false, power: false, api: '' } };
  const body = h('div', { class: 'stack', style: { gap: '20px' } });
  root.replaceChildren(body);

  loader(body, (signal) => api('clients', { signal }), (d, err) => {
    if (d) { st.clients = d; if (!st.form.client && d.length) { st.form.client = String(d[0].id); st.form.site = String(d[0].sites[0]?.id ?? ''); } }
    paint(err);
  }, 0, 'Loading clients…');

  const sitesOf = () => st.clients.find((c) => String(c.id) === String(st.form.client))?.sites ?? [];

  async function generate() {
    const f = st.form;
    st.busy = true; st.notice = null; st.result = null; paint();
    try {
      const payload = { client_id: Number(f.client), site_id: Number(f.site), agent_type: f.agent_type, arch: f.arch, expires_hours: Number(f.expires_hours), rdp: f.rdp, ping: f.ping, power: f.power };
      if (f.api.trim()) payload.api = f.api.trim();
      st.result = await api('installer/generate', { method: 'POST', body: payload });
    } catch (e) { st.notice = { ok: false, msg: e.message }; }
    st.busy = false; paint();
  }

  async function copy(key, text, node) {
    try { await navigator.clipboard.writeText(text); st.copied = key; }
    catch { const r = document.createRange(); r.selectNodeContents(node); const s = getSelection(); s.removeAllRanges(); s.addRange(r); st.copied = `${key}:select`; }
    paint();
  }

  function block(key, title, text, hint) {
    const pre = h('pre', { class: 'act-out', tabindex: '0' }, text);
    const label = st.copied === key ? 'Copied' : st.copied === `${key}:select` ? 'Selected, press Ctrl+C' : 'Copy';
    return h('div', { class: 'stack', style: { gap: '6px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } },
        h('span', { class: 'eyebrow' }, title), btn(label, () => copy(key, text, pre))),
      pre, hint ? h('span', { class: 'muted' }, hint) : null);
  }

  function resultCard(r) {
    const when = new Date(r.expires_at).toLocaleString();
    return h('section', { class: 'card stack', style: { gap: '16px' } },
      h('h2', { style: { fontSize: '16px' } }, `${r.client_name} / ${r.site_name} · ${r.agent_type} · ${r.arch === 'amd64' ? '64-bit' : '32-bit'}`),
      h('div', { class: 'muted' }, `Token expires ${when}. Server: ${r.api}`),
      r.warnings.map((w) => h('div', { class: 'banner', role: 'note' }, w)),
      h('div', { class: 'stack', style: { gap: '6px' } }, h('span', { class: 'eyebrow' }, 'Agent download'),
        h('a', { href: r.download_url, rel: 'noopener noreferrer', target: '_blank' }, r.file_name)),
      block('ps', 'PowerShell (run as Administrator) — downloads and installs', r.power_shell, 'Easiest option: one paste on the endpoint.'),
      block('cmd', 'Command Prompt (run as Administrator) — file already downloaded', r.command_prompt, 'Run from the folder that contains the downloaded file.'),
      block('token', 'Installer token', r.token));
  }

  function paint(err) {
    if (err) { body.replaceChildren(h('div', { class: 'banner', role: 'alert' }, err.message || String(err))); return; }
    if (!st.clients) return;
    const f = st.form;
    if (!st.clients.length) { body.replaceChildren(h('div', { class: 'page-head' }, h('h1', null, 'Install agent')), h('div', { class: 'empty' }, 'Create a client and a site first (Clients & Sites).')); return; }
    const sites = sitesOf();
    const clientSel = select(st.clients.map((c) => [c.id, c.name]), f, 'client');
    clientSel.addEventListener('change', () => { f.site = String(sitesOf()[0]?.id ?? ''); st.result = null; paint(); });
    body.replaceChildren(
      h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Install agent'),
        h('p', { class: 'muted' }, 'Generate a time-limited token and copy-ready commands to install the Tactical RMM agent on a Windows computer.'))),
      h('section', { class: 'card stack', style: { gap: '14px' } },
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Client', clientSel), field('Site', select(sites.map((s) => [s.id, s.name]), f, 'site')),
          field('Type', select([['workstation', 'Workstation'], ['server', 'Server']], f, 'agent_type'), '1 1 140px'),
          field('Architecture', select([['amd64', '64-bit'], ['386', '32-bit']], f, 'arch'), '1 1 120px'),
          field('Token valid for', select(HOURS, f, 'expires_hours'), '1 1 140px')),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '16px' } },
          check('Enable RDP', f, 'rdp'), check('Enable ping rule', f, 'ping'), check('Disable sleep / hibernate', f, 'power')),
        h('label', { class: 'stack', style: { gap: '4px' } }, h('span', { class: 'eyebrow' }, 'Server URL agents will use (optional, defaults to this page\'s address)'),
          (() => { const el = h('input', { type: 'text', class: 'search', style: { maxWidth: 'none', flex: 'none' }, placeholder: 'https://rmm.example.com', autocomplete: 'off', value: f.api }); el.addEventListener('input', () => { f.api = el.value; }); return el; })()),
        h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } },
          btn(st.busy ? 'Generating…' : 'Generate installer', generate, { primary: true, disabled: st.busy || !f.site }), notice(st.notice))),
      st.result ? resultCard(st.result) : null);
  }
}
