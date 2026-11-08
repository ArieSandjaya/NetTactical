// Safe DOM builder: all text goes through textContent/createTextNode, never innerHTML.
// Important because check/task output and device names come from agents (untrusted data).
const SVG_NS = 'http://www.w3.org/2000/svg';

function apply(el, attrs) {
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.setAttribute('class', v);
    else if (k === 'style') Object.assign(el.style, v);           // CSSOM: compatible with a CSP without 'unsafe-inline'
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
}
function append(el, kids) {
  for (const kid of kids.flat(Infinity)) {
    if (kid == null || kid === false) continue;
    el.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
  }
}
export function h(tag, attrs, ...kids) { const el = document.createElement(tag); apply(el, attrs); append(el, kids); return el; }
export function s(tag, attrs, ...kids) { const el = document.createElementNS(SVG_NS, tag); apply(el, attrs); append(el, kids); return el; }
export function clear(el) { el.replaceChildren(); return el; }
