// Shared "what am I editing" selector for the Checks and Tasks pages: one agent or one policy.
import { h } from './dom.js';
import { api } from './api.js';

export const scope = { kind: 'agent', id: '' };   // remembered while the dashboard stays open (also set by the Policies page)

export async function loadTargets(signal) {
  const [fleet, policies] = await Promise.all([
    api('fleet/assets', { signal }).catch(() => ({ assets: [] })),
    api('automation/policies', { signal }).catch(() => []),
  ]);
  return { agents: (fleet.assets || []).slice().sort((a, b) => a.hostname.localeCompare(b.hostname)), policies };
}

/** Select with two groups. Calls onChange after updating `scope`. */
export function scopeSelect(targets, onChange) {
  const val = `${scope.kind}:${scope.id}`;
  const el = h('select', { class: 'search', style: { maxWidth: '360px', flex: 'none' }, 'aria-label': 'Agent or policy' },
    h('optgroup', { label: 'Agents' }, targets.agents.map((a) => h('option', { value: `agent:${a.agent_id}`, selected: val === `agent:${a.agent_id}` }, `${a.hostname} · ${a.client} / ${a.site}`))),
    h('optgroup', { label: 'Policies' }, targets.policies.map((p) => h('option', { value: `policy:${p.id}`, selected: val === `policy:${p.id}` }, p.name))));
  el.addEventListener('change', () => { const [k, ...r] = el.value.split(':'); scope.kind = k; scope.id = r.join(':'); onChange(); });
  return el;
}

/** Makes sure `scope` points at something that exists; returns false when there is nothing to pick. */
export function ensureScope(targets) {
  const ok = scope.kind === 'agent' ? targets.agents.some((a) => a.agent_id === scope.id) : targets.policies.some((p) => String(p.id) === String(scope.id));
  if (ok) return true;
  if (targets.agents.length) { scope.kind = 'agent'; scope.id = targets.agents[0].agent_id; return true; }
  if (targets.policies.length) { scope.kind = 'policy'; scope.id = String(targets.policies[0].id); return true; }
  return false;
}

export const listPath = (what) => (scope.kind === 'agent' ? `agents/${scope.id}/${what}` : `automation/policies/${scope.id}/${what}`);
export const ownerFields = () => (scope.kind === 'agent' ? { agent: scope.id } : { policy: Number(scope.id) });
