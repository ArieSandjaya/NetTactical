import { h } from '../dom.js';
import { api } from '../api.js';
import { field, check, select, input, notice, btn } from '../forms.js';

const DAYS = [[1, '1 day'], [3, '3 days'], [7, '7 days'], [14, '14 days'], [30, '30 days']];
const STATUS = { pending: ['warn', 'Waiting'], running: ['info', 'Installing'], ok: ['good', 'Installed'], failed: ['crit', 'Failed'], skipped: ['neutral', 'Already there'], expired: ['crit', 'Expired'] };
const why = (e) => (e.status === 403 ? 'You do not have permission to manage software.' : e.message);
const blankPkg = () => ({ name: '', installer_type: 'msi', source: '', sha256: '', silent_args: '', timeout_seconds: '900', detect_name: '', note: '' });

// Install applications (for example Acrobat Reader) on many computers. A package says where the installer is and how to run it silently;
// a deployment sends a package to chosen clients, sites or computers. Offline computers are installed when they come online.
export function deploymentsView(root) {
  const st = { packages: null, deployments: null, clients: null, assets: null, pkgOpen: false, editing: null, pkg: blankPkg(), notice: null, busy: false,
    dep: { package_id: '', reinstall: false, expires_days: '7', clients: new Set(), sites: new Set(), agents: new Set(), search: '' }, open: new Set(), details: new Map() };
  const formBox = h('div', { class: 'stack', style: { gap: '20px' } });
  const listBox = h('div', { class: 'stack', style: { gap: '12px' } });
  root.replaceChildren(h('div', { class: 'stack', style: { gap: '20px' } },
    h('div', { class: 'page-head' }, h('div', { class: 'stack' }, h('h1', null, 'Deployments'),
      h('p', { class: 'muted' }, 'Install applications on many computers, for example Adobe Acrobat Reader. Computers that are off are installed as soon as they come online, until the deployment expires.'))),
    formBox, listBox));
  let alive = true, timer = null;

  async function loadAll() {
    try {
      const [packages, deployments, clients] = await Promise.all([api('software-packages'), api('app-deployments'), api('clients')]);
      if (!alive) return;
      Object.assign(st, { packages, deployments, clients });
      if (!st.dep.package_id && packages.length) st.dep.package_id = String(packages[0].id);
      paintForms(); paintList(); schedule();
    } catch (e) { if (alive) formBox.replaceChildren(h('div', { class: 'banner', role: 'alert' }, why(e))); }
  }

  const busyNow = () => (st.deployments || []).some((d) => d.pending + d.running > 0 && !d.expired);
  function schedule() { clearTimeout(timer); if (alive) timer = setTimeout(refresh, busyNow() ? 5000 : 30000); }
  async function refresh() {
    try {
      st.deployments = await api('app-deployments');
      for (const id of st.open) st.details.set(id, await api(`app-deployments/${id}`).catch(() => null));
      if (alive) paintList();
    } catch { /* keep what is shown; the next round tries again */ }
    schedule();
  }

  // ---- packages
  async function savePkg() {
    const p = st.pkg;
    st.busy = true; st.notice = null; paintForms();
    try {
      const body = { name: p.name, installer_type: p.installer_type, source: p.source, sha256: p.sha256 || null, silent_args: p.silent_args || null,
        timeout_seconds: Number(p.timeout_seconds) || 900, detect_name: p.detect_name || null, note: p.note || null };
      if (st.editing) await api(`software-packages/${st.editing}`, { method: 'PUT', body }); else await api('software-packages', { method: 'POST', body });
      st.packages = await api('software-packages');
      st.notice = { ok: true, msg: st.editing ? 'Package saved.' : 'Package added.' };
      st.pkgOpen = false; st.editing = null; st.pkg = blankPkg();
      if (!st.dep.package_id && st.packages.length) st.dep.package_id = String(st.packages[0].id);
    } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; paintForms();
  }
  function editPkg(p) {
    st.editing = p.id; st.pkgOpen = true; st.notice = null;
    st.pkg = { name: p.name, installer_type: p.installer_type, source: p.source, sha256: p.sha256 || '', silent_args: p.silent_args || '', timeout_seconds: String(p.timeout_seconds), detect_name: p.detect_name || '', note: p.note || '' };
    paintForms();
  }
  async function deletePkg(p) {
    if (!confirm(`Delete the package "${p.name}" and its finished deployments?`)) return;
    try { await api(`software-packages/${p.id}`, { method: 'DELETE' }); st.packages = await api('software-packages'); st.deployments = await api('app-deployments'); st.notice = { ok: true, msg: 'Package deleted.' }; if (String(p.id) === st.dep.package_id) st.dep.package_id = String(st.packages[0]?.id ?? ''); }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    paintForms(); paintList();
  }
  function fillAcrobat() {
    Object.assign(st.pkg, { name: st.pkg.name || 'Adobe Acrobat Reader', installer_type: 'exe', silent_args: '/sAll /rs /msi EULA_ACCEPT=YES', detect_name: 'Adobe Acrobat', timeout_seconds: '1200' });
    paintForms();
  }

  // ---- deployments
  async function start() {
    const d = st.dep;
    const pkg = st.packages.find((p) => String(p.id) === d.package_id);
    const body = { package_id: Number(d.package_id), reinstall: d.reinstall, expires_days: Number(d.expires_days), client_ids: [...d.clients], site_ids: [...d.sites], agent_ids: [...d.agents] };
    if (!body.client_ids.length && !body.site_ids.length && !body.agent_ids.length) { st.notice = { ok: false, msg: 'Choose at least one client, site or computer.' }; paintForms(); return; }
    if (!confirm(`Install ${pkg?.name ?? 'the package'} on the chosen Windows computers${d.reinstall ? ' (reinstall)' : ''}?`)) return;
    st.busy = true; st.notice = null; paintForms();
    try {
      const r = await api('app-deployments', { method: 'POST', body });
      st.notice = { ok: true, msg: `Started for ${r.total} computer${r.total === 1 ? '' : 's'}. Online computers are installed within a minute.` };
      Object.assign(st.dep, { clients: new Set(), sites: new Set(), agents: new Set(), search: '' });
      st.open.add(r.id); st.details.set(r.id, r);
      st.deployments = await api('app-deployments');
    } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.busy = false; paintForms(); paintList(); schedule();
  }
  async function toggle(d) {
    if (st.open.has(d.id)) st.open.delete(d.id);
    else { st.open.add(d.id); st.details.set(d.id, await api(`app-deployments/${d.id}`).catch(() => null)); }
    paintList();
  }
  async function retry(d) {
    try { st.details.set(d.id, await api(`app-deployments/${d.id}/retry`, { method: 'POST' })); st.deployments = await api('app-deployments'); st.notice = { ok: true, msg: 'Failed computers will be tried again.' }; }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    paintForms(); paintList(); schedule();
  }
  async function remove(d) {
    if (!confirm(`Delete this deployment of ${d.package_name}?\n\nComputers that are still waiting will not be installed. Programs already installed stay.`)) return;
    try { await api(`app-deployments/${d.id}`, { method: 'DELETE' }); st.open.delete(d.id); st.deployments = await api('app-deployments'); }
    catch (e) { st.notice = { ok: false, msg: why(e) }; paintForms(); }
    paintList();
  }
  async function loadAssets() {
    try { st.assets = (await api('fleet/assets')).assets.filter((a) => /windows/i.test(a.os || '') || !a.os); } catch (e) { st.assets = []; st.notice = { ok: false, msg: why(e) }; }
    paintForms();
  }

  // ---- painting
  const tick = (set, key, onChange) => { const el = h('input', { type: 'checkbox' }); el.checked = set.has(key); el.addEventListener('change', () => { if (el.checked) set.add(key); else set.delete(key); onChange?.(); }); return el; };

  function packageCard() {
    const p = st.pkg, isExe = p.installer_type === 'exe';
    const typeSel = select([['msi', '.msi (Windows Installer)'], ['exe', '.exe (setup program)']], p, 'installer_type');
    typeSel.addEventListener('change', () => paintForms());
    return h('section', { class: 'card stack', style: { gap: '12px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } },
        h('strong', null, 'Packages'), st.pkgOpen ? null : btn('New package', () => { st.pkgOpen = true; st.editing = null; st.pkg = blankPkg(); st.notice = null; paintForms(); })),
      st.packages.length ? h('div', { class: 'scroll-x' }, h('table', { class: 'tbl' },
        h('thead', null, h('tr', null, ['Name', 'Type', 'Installer', 'Skip if installed', ''].map((t) => h('th', null, t)))),
        h('tbody', null, st.packages.map((x) => h('tr', null,
          h('td', null, h('strong', null, x.name), x.note ? h('div', { class: 'muted' }, x.note) : null), h('td', null, `.${x.installer_type}`),
          h('td', { style: { wordBreak: 'break-all' } }, x.source), h('td', null, x.detect_name || '—'),
          h('td', null, h('div', { style: { display: 'flex', gap: '6px' } }, btn('Edit', () => editPkg(x)), btn('Delete', () => deletePkg(x), { danger: true})))))))) : h('div', { class: 'empty' }, 'No packages yet. Add one, for example Adobe Acrobat Reader.'),
      !st.pkgOpen ? null : h('div', { class: 'stack', style: { gap: '12px', borderTop: '1px solid var(--line, rgba(128,128,128,.25))', paddingTop: '12px' } },
        h('strong', null, st.editing ? 'Edit package' : 'New package'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Name', input('text', p, 'name', 'Adobe Acrobat Reader'), '2 1 220px'), field('Type', typeSel, '1 1 200px')),
        field('Where the installer is (https:// address or \\\\server\\share\\file)', input('text', p, 'source', isExe ? 'https://files.example.com/AcroRdrDC_en_US.exe' : 'https://files.example.com/app.msi')),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field(isExe ? 'Silent install options (required)' : 'Silent install options (default /qn /norestart)', input('text', p, 'silent_args', isExe ? '/S' : '/qn /norestart'), '2 1 260px'),
          field('Time limit (s)', input('number', p, 'timeout_seconds', '900'), '0 1 120px')),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Skip computers that already have (part of the program name, optional)', input('text', p, 'detect_name', 'Adobe Acrobat'), '2 1 260px'),
          field('SHA-256 of the file (optional, recommended)', input('text', p, 'sha256', '64 hexadecimal characters'), '2 1 260px')),
        field('Note (optional)', input('text', p, 'note', '')),
        h('div', { class: 'banner', role: 'note' }, 'Host the installer on a web server or file share you control, over https, and fill in the SHA-256 so a changed file is never installed. Use the vendor\'s offline (enterprise) installer, not the small downloader. Check the exact silent options on the vendor\'s site; wrong options make the installer wait for a click until the time limit.'),
        h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } },
          btn(st.busy ? 'Saving…' : 'Save package', savePkg, { primary: true, disabled: st.busy }),
          btn('Cancel', () => { st.pkgOpen = false; st.editing = null; paintForms(); }),
          st.editing ? null : btn('Fill in Acrobat Reader example', fillAcrobat))));
  }

  function targetPicker() {
    const d = st.dep;
    const repaint = () => paintForms();
    const sites = (c) => c.sites.map((s) => h('label', { class: 'check', style: { marginLeft: '22px' } }, (() => { const t = tick(d.sites, s.id); t.disabled = d.clients.has(c.id); return t; })(), h('span', null, s.name)));
    const list = (st.assets || []).filter((a) => !d.search || `${a.hostname} ${a.client} ${a.site}`.toLowerCase().includes(d.search.toLowerCase())).slice(0, 30);
    const search = h('input', { type: 'search', class: 'search', style: { maxWidth: 'none' }, placeholder: 'Search computers…', value: d.search, 'aria-label': 'Search computers' });
    search.addEventListener('input', () => { d.search = search.value; paintForms(); search.focus(); });
    return h('div', { class: 'stack', style: { gap: '10px' } },
      h('span', { class: 'eyebrow' }, 'Where to install (Windows computers only)'),
      h('div', { class: 'stack', style: { gap: '4px' } }, st.clients.map((c) => h('div', { class: 'stack', style: { gap: '2px' } },
        h('label', { class: 'check' }, tick(d.clients, c.id, repaint), h('strong', null, `${c.name} (all sites)`)), ...(d.clients.has(c.id) ? [] : sites(c))))),
      st.assets ? h('div', { class: 'stack', style: { gap: '6px' } }, search,
        h('div', { class: 'stack', style: { gap: '2px', maxHeight: '200px', overflow: 'auto' } }, list.map((a) => h('label', { class: 'check' }, tick(d.agents, a.agent_id), h('span', null, `${a.hostname} · ${a.client} / ${a.site}${a.online ? '' : ' · offline'}`)))),
        list.length === 30 ? h('span', { class: 'muted' }, 'Showing the first 30 matches. Type to narrow down.') : null)
        : h('div', null, btn('Pick individual computers…', loadAssets)));
  }

  function paintForms() {
    if (!st.packages) return;
    const d = st.dep;
    formBox.replaceChildren(...[
      packageCard(),
      !st.packages.length ? null : h('section', { class: 'card stack', style: { gap: '14px' } },
        h('strong', null, 'New deployment'),
        h('div', { style: { display: 'flex', flexWrap: 'wrap', gap: '12px' } },
          field('Package', select(st.packages.map((p) => [p.id, p.name]), d, 'package_id'), '2 1 240px'),
          field('Keep trying for', select(DAYS, d, 'expires_days'), '1 1 140px')),
        check('Install again even if the program is already there', d, 'reinstall'),
        targetPicker(),
        h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' } },
          btn(st.busy ? 'Starting…' : 'Start deployment', start, { primary: true, disabled: st.busy }))),
      notice(st.notice),
    ].filter(Boolean));
  }

  function row(d) {
    const open = st.open.has(d.id), det = st.details.get(d.id);
    const bar = [['ok', d.ok], ['skipped', d.skipped], ['pending', d.pending], ['running', d.running], ['failed', d.failed], ['expired', d.expired_count]].filter(([, n]) => n > 0)
      .map(([k, n]) => h('span', { class: `pill ${STATUS[k][0]}` }, `${n} ${STATUS[k][1].toLowerCase()}`));
    return h('section', { class: 'card stack', style: { gap: '10px' } },
      h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } },
        h('strong', null, `${d.package_name}${d.reinstall ? ' (reinstall)' : ''} · ${d.total} computer${d.total === 1 ? '' : 's'}`),
        h('div', { style: { display: 'flex', gap: '6px', flexWrap: 'wrap' } }, ...bar)),
      h('div', { class: 'muted' }, `Started ${new Date(d.created_at).toLocaleString()} by ${d.created_by || '?'} · ${d.expired ? 'expired' : 'keeps trying until'} ${new Date(d.expires_at).toLocaleString()}`),
      h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap' } }, btn(open ? 'Hide computers' : 'Show computers', () => toggle(d)),
        d.failed + d.expired_count > 0 ? btn('Retry failed', () => retry(d)) : null, btn('Delete', () => remove(d), { danger: true })),
      !open ? null : !det ? h('div', { class: 'loading' }, 'Loading…') : h('div', { class: 'scroll-x' }, h('table', { class: 'tbl' },
        h('thead', null, h('tr', null, ['Computer', 'Client / site', 'Status', 'Result'].map((t) => h('th', null, t)))),
        h('tbody', null, det.targets.map((t) => h('tr', null, h('td', null, t.hostname), h('td', null, `${t.client_name} / ${t.site_name}`),
          h('td', null, h('span', { class: `pill ${STATUS[t.status]?.[0] || 'neutral'}` }, STATUS[t.status]?.[1] || t.status)), h('td', null, t.message || '—')))))));
  }

  function paintList() {
    if (!st.deployments) return;
    listBox.replaceChildren(...(st.deployments.length ? st.deployments.map(row) : [h('div', { class: 'empty' }, 'No deployments yet.')]));
  }

  loadAll();
  return () => { alive = false; clearTimeout(timer); };
}
