import { h, s } from './dom.js';
import { session, logout } from './api.js';
import { loginView } from './views/login.js';
import { dashboardView } from './views/dashboard.js';
import { deviceView } from './views/device.js';

const app = document.getElementById('app');
let cleanup = () => {};

function shell(content, current) {
  const mark = s('svg', { width: 28, height: 28, viewBox: '0 0 28 28', 'aria-hidden': 'true' },
    s('rect', { x: 2, y: 2, width: 24, height: 24, rx: 6, fill: '#0b6e8a' }),
    s('path', { d: 'M8 19L12 13L16 16L20 8', fill: 'none', stroke: '#fff', 'stroke-width': 2.2, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }));
  return h('div', { class: 'shell' },
    h('aside', { class: 'side' },
      h('div', { class: 'brand' }, mark, 'NetTactical'),
      h('nav', { 'aria-label': 'Navigasi utama' }, h('a', { href: '#/', 'aria-current': current === 'home' ? 'page' : null }, 'Ringkasan')),
      h('div', { class: 'who' }, h('button', { onclick: async () => { await logout(); session.clear(); location.hash = '#/login'; } }, 'Keluar'))),
    h('main', { class: 'main', id: 'view' }, content));
}

function route() {
  cleanup(); cleanup = () => {};
  const hash = location.hash || '#/';
  if (hash === '#/login') { cleanup = loginView(app); document.title = 'Masuk · NetTactical'; return; }
  if (!session.token) { location.hash = '#/login'; return; }

  const holder = h('div');
  const m = hash.match(/^#\/device\/(.+)$/);
  app.replaceChildren(shell(holder, m ? 'device' : 'home'));
  if (m) { let id; try { id = decodeURIComponent(m[1]); } catch { id = m[1]; } document.title = `${id} · NetTactical`; cleanup = deviceView(holder, id); }
  else { document.title = 'Kesehatan aset · NetTactical'; cleanup = dashboardView(holder); }
  window.scrollTo(0, 0);
}

window.addEventListener('hashchange', route);
route();
