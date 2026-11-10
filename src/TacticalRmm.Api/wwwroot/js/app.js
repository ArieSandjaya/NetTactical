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

const app = document.getElementById('app');
let cleanup = () => {};
const NAV = [['home', '#/', 'Overview'], ['assets', '#/assets', 'Assets'], ['alerts', '#/alerts', 'Alerts'], ['patches', '#/patches', 'Patches'], ['section', '', 'Monitoring'], ['checks', '#/checks', 'Checks'], ['tasks', '#/tasks', 'Tasks'], ['policies', '#/policies', 'Policies'], ['scripts', '#/scripts', 'Scripts'], ['audit', '#/audit', 'Audit log'], ['section', '', 'Manage'], ['clients', '#/clients', 'Clients & Sites'], ['users', '#/users', 'Users'], ['roles', '#/roles', 'Roles'], ['installer', '#/installer', 'Install agent'], ['security', '#/security', 'Security'], ['vnc', '#/vnc', 'VNC passwords']];
const PAGES = { '#/assets': ['assets', 'Assets', assetsView], '#/alerts': ['alerts', 'Alerts', alertsView], '#/patches': ['patches', 'Patches', patchesView], '#/vnc': ['vnc', 'VNC passwords', vncView],
  '#/clients': ['clients', 'Clients & Sites', clientsView], '#/users': ['users', 'Users', usersView], '#/roles': ['roles', 'Roles', rolesView], '#/installer': ['installer', 'Install agent', installerView], '#/security': ['security', 'Security', securityView],
  '#/checks': ['checks', 'Checks', checksView], '#/tasks': ['tasks', 'Tasks', tasksView], '#/policies': ['policies', 'Policies', policiesView], '#/scripts': ['scripts', 'Scripts', scriptsView], '#/audit': ['audit', 'Audit log', auditView] };

function shell(content, current) {
  const mark = s('svg', { width: 28, height: 28, viewBox: '0 0 28 28', 'aria-hidden': 'true' },
    s('rect', { x: 2, y: 2, width: 24, height: 24, rx: 6, fill: '#0b6e8a' }),
    s('path', { d: 'M8 19L12 13L16 16L20 8', fill: 'none', stroke: '#fff', 'stroke-width': 2.2, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }));
  return h('div', { class: 'shell' },
    h('aside', { class: 'side' },
      h('div', { class: 'brand' }, mark, 'NetTactical'),
      h('nav', { 'aria-label': 'Main navigation' }, NAV.map(([key, href, label]) => (key === 'section' ? h('div', { class: 'nav-section' }, label) : h('a', { href, 'aria-current': current === key ? 'page' : null }, label)))),
      h('div', { class: 'who' }, h('button', { onclick: async () => { await logout(); session.clear(); location.hash = '#/login'; } }, 'Sign out'))),
    h('main', { class: 'main', id: 'view' }, content));
}

function route() {
  cleanup(); cleanup = () => {};
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
