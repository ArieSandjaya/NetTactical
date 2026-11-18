import { h } from '../dom.js';
import { api } from '../api.js';
import { ago, fmtDate } from '../format.js';
import { btn, notice } from '../forms.js';

// Free-text notes about one asset (who did what, serial numbers, "RAM replaced"). Created once per page and reused,
// so a note being typed is not lost when the page repaints.
export function notesPanel(agentId) {
  const st = { notes: null, err: null, text: '', busy: false, notice: null };
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });
  const base = `agents/${encodeURIComponent(agentId)}/notes`;
  const box = h('textarea', { rows: '3', maxlength: '5000', placeholder: 'Write a note about this asset…', 'aria-label': 'New note', style: { width: '100%', resize: 'vertical' } });
  box.addEventListener('input', () => { st.text = box.value; });

  async function load() { try { st.notes = await api(base); st.err = null; } catch (e) { st.err = e.message; } paint(); }
  async function add() {
    if (!st.text.trim()) { st.notice = { ok: false, msg: 'Write something first.' }; paint(); return; }
    st.busy = true; st.notice = null; paint();
    try { await api(base, { method: 'POST', body: { note: st.text } }); st.text = ''; box.value = ''; st.notes = await api(base); }
    catch (e) { st.notice = { ok: false, msg: e.status === 403 ? 'You do not have permission to edit this asset.' : e.message }; }
    st.busy = false; paint();
  }
  async function remove(n) {
    if (!confirm('Delete this note?')) return;
    try { await api(`${base}/${n.id}`, { method: 'DELETE' }); st.notes = await api(base); st.notice = null; }
    catch (e) { st.notice = { ok: false, msg: e.status === 403 ? 'You do not have permission to edit this asset.' : e.message }; }
    paint();
  }

  function paint() {
    if (st.err) { root.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    root.replaceChildren(...[
      box,
      h('div', null, btn(st.busy ? 'Saving…' : 'Add note', add, { primary: true, disabled: st.busy })),
      notice(st.notice),
      !st.notes ? h('div', { class: 'muted' }, 'Loading notes…')
        : st.notes.length === 0 ? h('div', { class: 'muted' }, 'No notes yet.')
          : h('div', { class: 'stack', style: { gap: '10px' } }, st.notes.map((n) => h('div', { class: 'card', style: { padding: '12px 14px', gap: '6px' } },
            h('div', { style: { whiteSpace: 'pre-wrap', wordBreak: 'break-word' } }, n.note),
            h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center', justifyContent: 'space-between' } },
              h('span', { class: 'muted', title: fmtDate(n.entry_time) }, `${n.username || 'unknown'} · ${ago(n.entry_time)}`),
              btn('Delete', () => remove(n), { danger: true })))))].filter(Boolean));   // replaceChildren turns null into the text "null"
  }

  paint(); load();
  return { el: root };
}

const TYPES = { cmd_run: 'Command', script_run: 'Script', task_run: 'Task' };

// What was run on the asset (commands, scripts, tasks), newest first, with the result when there is one.
export function historyPanel(agentId) {
  const st = { rows: null, err: null, busy: false };
  const root = h('div', { class: 'pad stack', style: { gap: '14px' } });
  async function load() { st.busy = true; paint(); try { st.rows = await api(`agents/${encodeURIComponent(agentId)}/history?take=100`); st.err = null; } catch (e) { st.err = e.message; } st.busy = false; paint(); }

  const result = (r) => {
    if (r.stdout == null && r.stderr == null && r.retcode == null) return null;
    const parts = [r.retcode != null ? `Exit code ${r.retcode}${r.execution_time != null ? ` · ${r.execution_time.toFixed(1)} s` : ''}` : null, r.stdout, r.stderr ? `Error output:\n${r.stderr}` : null].filter(Boolean);
    return h('details', null, h('summary', { class: r.retcode ? 'pill crit' : 'pill good', style: { cursor: 'pointer' } }, r.retcode ? 'Failed' : 'Result'), h('pre', { class: 'act-out' }, parts.join('\n\n')));
  };

  function paint() {
    if (st.err) { root.replaceChildren(h('div', { class: 'banner', role: 'alert' }, st.err)); return; }
    root.replaceChildren(...[
      h('div', null, btn(st.busy ? 'Loading…' : 'Refresh', load, { disabled: st.busy })),
      !st.rows ? h('div', { class: 'muted' }, 'Loading history…')
        : st.rows.length === 0 ? h('div', { class: 'muted' }, 'Nothing has been run on this asset from the dashboard yet.')
          : h('div', { class: 'scroll-x' }, h('table', { class: 'report-table' },
            h('thead', null, h('tr', null, ['When', 'Type', 'What', 'By', 'Result'].map((c) => h('th', { scope: 'col' }, c)))),
            h('tbody', null, st.rows.map((r) => h('tr', null,
              h('td', { title: fmtDate(r.time) }, ago(r.time)), h('td', null, TYPES[r.type] || r.type),
              h('td', { class: 'mono', style: { whiteSpace: 'pre-wrap', wordBreak: 'break-word', maxWidth: '380px' } }, r.command || ''),
              h('td', null, r.username || ''), h('td', null, result(r))))))),
    ].filter(Boolean));
  }

  paint(); load();
  return { el: root };
}
