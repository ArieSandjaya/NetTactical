import { s } from './dom.js';
import { fmtDay } from './format.js';

const W = 640, H = 220, L = 44, R = 624, T = 16, B = 188;

/** 0–100% line chart from points {t: ISO, v: number}. The X axis is proportional to time. */
export function lineChart(points, { threshold = 90, label = 'Chart' } = {}) {
  const svg = s('svg', { class: 'chart', viewBox: `0 0 ${W} ${H}`, role: 'img', 'aria-label': label });
  const y = (v) => B - (Math.min(100, Math.max(0, v)) / 100) * (B - T);

  for (const g of [0, 25, 50, 75, 100]) {
    svg.append(s('line', { class: 'grid', x1: L, x2: R, y1: y(g), y2: y(g) }));
    svg.append(s('text', { class: 'axis', x: L - 8, y: y(g) + 4, 'text-anchor': 'end' }, String(g)));
  }
  svg.append(s('line', { class: 'thr', x1: L, x2: R, y1: y(threshold), y2: y(threshold) }));
  svg.append(s('text', { class: 'thr-label', x: R, y: y(threshold) - 6, 'text-anchor': 'end' }, `Critical threshold ${threshold}%`));

  if (!points.length) {
    svg.append(s('text', { class: 'axis', x: (L + R) / 2, y: (T + B) / 2, 'text-anchor': 'middle' }, 'No data for this range yet'));
    return svg;
  }

  const ts = points.map((p) => new Date(p.t).getTime());
  const t0 = Math.min(...ts), t1 = Math.max(...ts);
  const x = (t) => (t1 === t0 ? (L + R) / 2 : L + ((t - t0) / (t1 - t0)) * (R - L));
  const xy = points.map((p, i) => [x(ts[i]), y(p.v)]);
  const path = xy.map(([px, py]) => `${px.toFixed(1)},${py.toFixed(1)}`).join(' ');

  if (xy.length > 1) {
    svg.append(s('polygon', { class: 'area', points: `${xy[0][0].toFixed(1)},${B} ${path} ${xy.at(-1)[0].toFixed(1)},${B}` }));
    svg.append(s('polyline', { class: 'line', points: path }));
  }
  const [ex, ey] = xy.at(-1);
  svg.append(s('circle', { class: 'end', cx: ex.toFixed(1), cy: ey.toFixed(1), r: 5 }));

  const labels = [[L, 'start', t0], [(L + R) / 2, 'middle', (t0 + t1) / 2], [R, 'end', t1]];
  for (const [lx, anchor, t] of labels)
    svg.append(s('text', { class: 'axis', x: lx, y: 210, 'text-anchor': anchor }, fmtDay(new Date(t).toISOString())));
  return svg;
}

/** Score ring, 0–100. */
export function ring(score, cls) {
  const r = 52, c = 2 * Math.PI * r;
  const svg = s('svg', { class: 'ring', width: 124, height: 124, viewBox: '0 0 124 124', role: 'img', 'aria-label': `Health score ${score} out of 100` });
  svg.append(s('circle', { class: 'track', cx: 62, cy: 62, r, fill: 'none', 'stroke-width': 12 }));
  const arc = s('circle', { cx: 62, cy: 62, r, fill: 'none', 'stroke-width': 12, 'stroke-linecap': 'round',
    'stroke-dasharray': `${((score / 100) * c).toFixed(1)} ${c.toFixed(1)}`, transform: 'rotate(-90 62 62)' });
  arc.style.stroke = `var(--${cls})`;
  svg.append(arc);
  svg.append(s('text', { x: 62, y: 70, 'text-anchor': 'middle', 'font-size': 32 }, String(score)));
  return svg;
}
