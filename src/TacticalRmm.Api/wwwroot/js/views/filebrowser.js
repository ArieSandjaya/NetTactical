import { h } from '../dom.js';
import { api, download } from '../api.js';
import { btn, notice } from '../forms.js';

const MAX_UPLOAD = 8 * 1024 * 1024;
const why = (e) => (e.status === 403 ? 'You do not have permission to run commands on this asset.' : e.message);
const size = (n) => (n == null ? '' : n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : n < 1073741824 ? `${(n / 1048576).toFixed(1)} MB` : `${(n / 1073741824).toFixed(1)} GB`);
const when = (iso) => { const d = iso ? new Date(iso) : null; return d && !Number.isNaN(d.getTime()) ? d.toLocaleString() : ''; };
const join = (dir, name) => `${dir.replace(/[\\/]+$/, '')}\\${name}`;
const b64 = (buf) => { let s = ''; const a = new Uint8Array(buf); for (let i = 0; i < a.length; i += 0x8000) s += String.fromCharCode(...a.subarray(i, i + 0x8000)); return btoa(s); };

// File browser of one Windows asset. Every step is a live request to the agent (a folder can take a few seconds). Created once per page.
export function filesPanel(agentId) {
  const st = { path: '', list: null, err: null, busy: false, notice: null, acting: null, pathText: '' };
  const base = `agents/${encodeURIComponent(agentId)}/files`;
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });
  const pathBox = h('input', { type: 'text', class: 'search', 'aria-label': 'Folder path', placeholder: 'C:\\  (empty = drives)', style: { maxWidth: 'none', flex: '1 1 260px', fontFamily: 'var(--mono)' } });
  pathBox.addEventListener('keydown', (e) => { if (e.key === 'Enter') open(pathBox.value.trim()); });
  const fileBox = h('input', { type: 'file', hidden: true });
  fileBox.addEventListener('change', () => { const f = fileBox.files[0]; fileBox.value = ''; if (f) upload(f); });

  async function open(path) {
    st.busy = true; st.err = null; st.notice = null; paint();
    try { st.list = await api(`${base}?path=${encodeURIComponent(path)}`); st.path = st.list.path; pathBox.value = st.path; }
    catch (e) { st.err = why(e); }
    st.busy = false; paint();
  }
  const reload = () => open(st.path);

  async function run(label, fn, done) {
    st.acting = label; st.notice = null; paint();
    try { const r = await fn(); st.notice = { ok: true, msg: (r && r.message) || done }; st.acting = null; await reload(); return; }
    catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.acting = null; paint();
  }

  async function get(entry) {
    st.acting = `dl:${entry.name}`; st.notice = null; paint();
    try {
      const { blob, filename } = await download(`${base}/download?path=${encodeURIComponent(join(st.path, entry.name))}`, { method: 'GET' });
      const url = URL.createObjectURL(blob); const link = h('a', { href: url, download: filename });
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 10000);
      st.notice = { ok: true, msg: `${filename} downloaded (${size(blob.size)}).` };
    } catch (e) { st.notice = { ok: false, msg: why(e) }; }
    st.acting = null; paint();
  }

  async function upload(file) {
    if (file.size > MAX_UPLOAD) { st.notice = { ok: false, msg: `The file is larger than ${MAX_UPLOAD / 1048576} MB.` }; paint(); return; }
    const exists = (st.list?.entries || []).some((e) => e.name.toLowerCase() === file.name.toLowerCase());
    if (exists && !confirm(`"${file.name}" already exists here. Replace it?`)) return;
    await run('upload', async () => api(`${base}/upload`, { method: 'POST', body: { path: st.path, name: file.name, content_base64: b64(await file.arrayBuffer()), overwrite: exists } }), 'Uploaded.');
  }

  const newFolder = () => {
    const name = (prompt('Name of the new folder') || '').trim();
    if (name) run('folder', () => api(`${base}/folder`, { method: 'POST', body: { path: st.path, name } }), 'Folder created.');
  };
  const rename = (e) => {
    const name = (prompt(`New name for "${e.name}"`, e.name) || '').trim();
    if (name && name !== e.name) run(`ren:${e.name}`, () => api(`${base}/rename`, { method: 'POST', body: { path: join(st.path, e.name), new_name: name } }), 'Renamed.');
  };
  const remove = (e) => {
    const target = join(st.path, e.name);
    if (!confirm(e.is_dir ? `Delete the folder "${target}" and everything in it?\n\nThis cannot be undone.` : `Delete the file "${target}"?\n\nThis cannot be undone.`)) return;
    run(`del:${e.name}`, () => api(`${base}/delete`, { method: 'POST', body: { path: target, recursive: !!e.is_dir } }), 'Deleted.');
  };

  function paint() {
    const l = st.list; const top = l && l.path === '';
    const busy = st.busy || !!st.acting;
    root.replaceChildren(...[
      h('div', { style: { display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' } },
        btn('Drives', () => open(''), { disabled: busy }),
        btn('Up', () => open(l?.parent ?? ''), { disabled: busy || !l || top }),
        pathBox,
        btn('Go', () => open(pathBox.value.trim()), { primary: true, disabled: busy }),
        btn('Refresh', reload, { disabled: busy || !l })),
      l && !top ? h('div', { style: { display: 'flex', gap: '8px', flexWrap: 'wrap' } },
        btn(st.acting === 'upload' ? 'Uploading…' : 'Upload file', () => fileBox.click(), { disabled: busy }),
        btn('New folder', newFolder, { disabled: busy })) : null,
      h('span', { class: 'muted', style: { fontSize: '12px' } }, 'Runs as the agent account (normally SYSTEM), so it sees every file on the computer. Downloads and uploads are limited to 8 MB. Every action is logged.'),
      notice(st.notice),
      st.err ? h('div', { class: 'banner', role: 'alert' }, st.err) : null,
      st.busy ? h('span', { class: 'muted' }, 'Asking the agent…') : null,
      l && !st.busy ? h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
        h('thead', null, h('tr', null, [top ? 'Drive' : 'Name', top ? 'Free / used' : 'Size', 'Modified', ''].map((c) => h('th', { scope: 'col' }, c)))),
        h('tbody', null, l.entries.map((e) => h('tr', null,
          h('td', null, e.is_dir
            ? h('a', { href: '#', onclick: (ev) => { ev.preventDefault(); if (!busy) open(top ? e.name : join(st.path, e.name)); } }, `📁 ${e.name}`)
            : h('span', null, `📄 ${e.name}`)),
          h('td', { class: 'mono' }, top ? (e.free != null ? `${size(e.free)} free of ${size((e.free || 0) + (e.used || 0))}` : '') : size(e.size)),
          h('td', null, when(e.modified)),
          h('td', { style: { textAlign: 'right', whiteSpace: 'nowrap' } }, top ? null : [
            e.is_dir ? null : btn(st.acting === `dl:${e.name}` ? 'Downloading…' : 'Download', () => get(e), { disabled: busy }), ' ',
            btn('Rename', () => rename(e), { disabled: busy }), ' ',
            btn(st.acting === `del:${e.name}` ? 'Deleting…' : 'Delete', () => remove(e), { danger: true, disabled: busy })])))),
        l.entries.length ? null : h('caption', { class: 'empty' }, 'This folder is empty.'))) : null,
      l?.truncated ? h('span', { class: 'muted' }, 'Only the first 3000 items are shown.') : null,
      fileBox,
    ].flat().filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint();
  return { el: root, start: () => { if (!st.list && !st.busy) open(''); } };
}
