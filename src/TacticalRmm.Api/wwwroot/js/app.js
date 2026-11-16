import { h, s } from './dom.js';
import { session, logout } from './api.js';
import { loginView } from './views/login.js';
import { dashboardView } from './views/dashboard.js';
import { deviceView } from './views/device.js';
import { assetsView } from './views/assets.js';
import { alertsView } from './views/alerts.js';
import { patchesView } from './views/patches.js';
import { vncView } from './views/vnc.js';
import { clientsView } from './views/clients.js';
import { installerView } from './views/installer.js';
import { usersView } from './views/users.js';
import { rolesView } from './views/roles.js';
import { securityView } from './views/security.js';
import { scriptsView } from './views/scripts.js';
import { policiesView } from './views/policies.js';
import { checksView } from './views/checks.js';
import { tasksView } from './views/tasks.js';
import { auditView } from './views/audit.js';
import { notificationsView } from './views/notifications.js';
import { alertTemplatesView } from './views/alerttemplates.js';
import { customFieldsView } from './views/customfields.js';
import { snippetsView } from './views/snippets.js';
import { reportsView } from './views/reports.js';

const app = document.getElementById('app');
let cleanup = () => {};
const NAV = [['home', '#/', 'Overview'], ['assets', '#/assets', 'Assets'], ['alerts', '#/alerts', 'Alerts'], ['patches', '#/patches', 'Patches'], ['section', '', 'Monitoring'], ['checks', '#/checks', 'Checks'], ['tasks', '#/tasks', 'Tasks'], ['policies', '#/policies', 'Policies'], ['scripts', '#/scripts', 'Scripts'], ['snippets', '#/snippets', 'Snippets'], ['reports', '#/reports', 'Reports'], ['audit', '#/audit', 'Audit log'], ['section', '', 'Manage'], ['clients', '#/clients', 'Clients & Sites'], ['users', '#/users', 'Users'], ['roles', '#/roles', 'Roles'], ['notifications', '#/notifications', 'Notifications'], ['alerttemplates', '#/alert-templates', 'Alert templates'], ['customfields', '#/custom-fields', 'Custom fields'], ['installer', '#/installer', 'Install agent'], ['security', '#/security', 'Security'], ['vnc', '#/vnc', 'VNC passwords']];
const PAGES = { '#/assets': ['assets', 'Assets', assetsView], '#/alerts': ['alerts', 'Alerts', alertsView], '#/patches': ['patches', 'Patches', patchesView], '#/vnc': ['vnc', 'VNC passwords', vncView],
  '#/clients': ['clients', 'Clients & Sites', clientsView], '#/users': ['users', 'Users', usersView], '#/roles': ['roles', 'Roles', rolesView], '#/installer': ['installer', 'Install agent', installerView], '#/security': ['security', 'Security', securityView],
  '#/checks': ['checks', 'Checks', checksView], '#/tasks': ['tasks', 'Tasks', tasksView], '#/policies': ['policies', 'Policies', policiesView], '#/scripts': ['scripts', 'Scripts', scriptsView], '#/audit': ['audit', 'Audit log', auditView], '#/notifications': ['notifications', 'Notifications', notificationsView], '#/alert-templates': ['alerttemplates', 'Alert templates', alertTemplatesView], '#/custom-fields': ['customfields', 'Custom fields', customFieldsView], '#/snippets': ['snippets', 'Snippets', snippetsView], '#/reports': ['reports', 'Reports', reportsView] };

// Line icons for the sidebar (24x24 grid, drawn with the current text colour).
const ICONS = {
  home: ['M3 3h7v7H3z', 'M14 3h7v7h-7z', 'M14 14h7v7h-7z', 'M3 14h7v7H3z'],
  assets: ['M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z', 'M8 21h8', 'M12 17v4'],
  alerts: ['M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9', 'M13.7 21a2 2 0 0 1-3.4 0'],
  patches: ['M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4', 'M7 10l5 5 5-5', 'M12 15V3'],
  checks: ['M22 11.1V12a10 10 0 1 1-5.9-9.1', 'M22 4L12 14l-3-3'],
  tasks: ['M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20z', 'M12 6v6l4 2'],
  policies: ['M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z'],
  scripts: ['M4 17l6-6-6-6', 'M12 19h8'],
  audit: ['M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z', 'M14 2v6h6', 'M16 13H8', 'M16 17H8', 'M10 9H8'],
  clients: ['M4 7h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z', 'M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16'],
  users: ['M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2', 'M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z', 'M23 21v-2a4 4 0 0 0-3-3.9', 'M16 3.1a4 4 0 0 1 0 7.8'],
  roles: ['M12 2l8 3v6c0 5-3.5 9-8 11-4.5-2-8-6-8-11V5z', 'M9 12l2 2 4-4'],
  notifications: ['M22 2L11 13', 'M22 2l-7 20-4-9-9-4z'],
  reports: ['M5 20V10', 'M12 20V4', 'M19 20v-7'],
  snippets: ['M8 6l-5 6 5 6', 'M16 6l5 6-5 6', 'M14 4l-4 16'],
  customfields: ['M4 5h16', 'M4 12h16', 'M4 19h10', 'M18 17v4', 'M16 19h4'],
  alerttemplates: ['M4 21v-7', 'M4 10V3', 'M12 21v-9', 'M12 8V3', 'M20 21v-5', 'M20 12V3', 'M1 14h6', 'M9 8h6', 'M17 16h6'],
  installer: ['M22 12H2', 'M5.5 5.1L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z', 'M6 16h.01', 'M10 16h.01'],
  security: ['M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z', 'M7 11V7a5 5 0 0 1 10 0v4'],
  vnc: ['M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z', 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z'],
  menu: ['M3 6h18', 'M3 12h18', 'M3 18h18'],
  close: ['M18 6L6 18', 'M6 6l12 12'],
  sun: ['M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10z', 'M12 1v2', 'M12 21v2', 'M4.2 4.2l1.4 1.4', 'M18.4 18.4l1.4 1.4', 'M1 12h2', 'M21 12h2', 'M4.2 19.8l1.4-1.4', 'M18.4 5.6l1.4-1.4'],
  moon: ['M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z'],
};
const icon = (name) => s('svg', { class: 'ico', viewBox: '0 0 24 24', 'aria-hidden': 'true' }, (ICONS[name] || []).map((d) => s('path', { d })));

// Sidebar visibility. Wide screens: the sidebar can be hidden for good (remembered). Narrow screens: it is a drawer that opens over the page and
// closes on its own after a choice. State lives on <html> (data-nav / data-drawer) because the shell is rebuilt on every page change.
const root = document.documentElement;
const narrow = () => window.matchMedia('(max-width: 720px)').matches;
const menuOpen = () => (narrow() ? root.getAttribute('data-drawer') === 'open' : root.getAttribute('data-nav') !== 'closed');
function syncMenu() {
  const btn = document.querySelector('.menu-btn');
  if (btn) { btn.setAttribute('aria-expanded', String(menuOpen())); }
}
function setMenu(open, { focus = false } = {}) {
  if (narrow()) { if (open) root.setAttribute('data-drawer', 'open'); else root.removeAttribute('data-drawer'); }
  else { root.setAttribute('data-nav', open ? 'open' : 'closed'); try { localStorage.setItem('nt.nav', open ? 'open' : 'closed'); } catch { /* not remembered */ } }
  syncMenu();
  if (focus) { const t = open ? document.querySelector('.side nav a') : document.querySelector('.menu-btn'); if (t) t.focus(); }
}
window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && narrow() && menuOpen()) setMenu(false, { focus: true }); });
window.matchMedia('(max-width: 720px)').addEventListener('change', () => { root.removeAttribute('data-drawer'); syncMenu(); });
const menuButton = () => h('button', { type: 'button', class: 'menu-btn', 'aria-label': 'Show or hide the menu', 'aria-controls': 'side', 'aria-expanded': String(menuOpen()), onclick: () => setMenu(!menuOpen(), { focus: narrow() }) }, icon('menu'));

// Colour theme switch (the choice is remembered in this browser; storage may be blocked, so every access is guarded).
function themeToggle() {
  const btn = h('button', { type: 'button' });
  const dark = () => document.documentElement.getAttribute('data-theme') === 'dark';
  const paint = () => btn.replaceChildren(icon(dark() ? 'sun' : 'moon'), dark() ? 'Light mode' : 'Dark mode');
  btn.addEventListener('click', () => {
    const next = dark() ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    try { localStorage.setItem('nt.theme', next); } catch { /* not remembered */ }
    paint();
  });
  paint();
  return btn;
}

function shell(content, current) {
  const mark = s('svg', { width: 28, height: 28, viewBox: '0 0 28 28', 'aria-hidden': 'true' },
    s('defs', null, s('linearGradient', { id: 'nt-logo', x1: 0, y1: 0, x2: 1, y2: 1 }, s('stop', { offset: '0', 'stop-color': '#059669' }), s('stop', { offset: '1', 'stop-color': '#c58b4a' }))),
    s('rect', { x: 2, y: 2, width: 24, height: 24, rx: 8, fill: 'url(#nt-logo)' }),
    s('path', { d: 'M8 19L12 13L16 16L20 8', fill: 'none', stroke: '#fff', 'stroke-width': 2.2, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }));
  return h('div', { class: 'shell' },
    h('div', { class: 'scrim', onclick: () => setMenu(false) }),
    h('aside', { class: 'side', id: 'side' },
      h('div', { class: 'brand' }, mark, h('span', { class: 'brand-name' }, 'NetTactical'),
        h('button', { type: 'button', class: 'side-close', 'aria-label': 'Close the menu', onclick: () => setMenu(false, { focus: true }) }, icon('close'))),
      h('nav', { 'aria-label': 'Main navigation' }, NAV.map(([key, href, label]) => (key === 'section' ? h('div', { class: 'nav-section' }, label) : h('a', { href, 'aria-current': current === key ? 'page' : null }, icon(key), label)))),
      h('div', { class: 'who' }, themeToggle(), h('button', { onclick: async () => { await logout(); session.clear(); location.hash = '#/login'; } }, 'Sign out'),
        h('a', { class: 'credit', href: 'https://templatemo.com/tm-607-glass-admin', target: '_blank', rel: 'noopener nofollow' }, 'Design: TemplateMo'))),
    h('main', { class: 'main', id: 'view' }, h('div', { class: 'topbar' }, menuButton(), h('span', { class: 'topbar-brand' }, 'NetTactical')), content));
}

function route() {
  cleanup(); cleanup = () => {};
  root.removeAttribute('data-drawer');
  const hash = location.hash || '#/';
  if (hash === '#/login') { cleanup = loginView(app); document.title = 'Sign in · NetTactical'; return; }
  if (!session.token) { location.hash = '#/login'; return; }

  const holder = h('div');
  const m = hash.match(/^#\/device\/(.+)$/);
  const page = PAGES[hash];
  app.replaceChildren(shell(holder, page ? page[0] : m ? 'assets' : 'home'));
  if (m) { let id; try { id = decodeURIComponent(m[1]); } catch { id = m[1]; } document.title = `${id} · NetTactical`; cleanup = deviceView(holder, id); }
  else if (page) { document.title = `${page[1]} · NetTactical`; cleanup = page[2](holder); }
  else { document.title = 'Asset health · NetTactical'; cleanup = dashboardView(holder); }
  window.scrollTo(0, 0);
}

window.addEventListener('hashchange', route);
route();
